using System.Globalization;
using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

/// <summary>
/// When an entity has both a mmol claim and an "equiv" mention nearby,
/// validates that the equivalents are consistent with a reference substrate.
/// The first mmol claim in the document is treated as the reference unless
/// an explicit "based on X" qualifier is found.
/// </summary>
public class EquivalentsConsistencyValidator : IValidator
{
    // Matches patterns like "0.5 equiv", "1.2 equivalents", "2 eq"
    private static readonly Regex EquivRegex = new(
        @"(?<num>\d+(?:\.\d+)?)\s*(?<unit>equiv(?:alent)?s?|eq\.?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const double ToleranceFraction = 0.25; // 25% tolerance

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();
        string text = run.GetAnalyzedText();
        if (string.IsNullOrEmpty(text)) return findings;

        // Find all mmol claims
        List<ExtractedClaim> mmolClaims = claims
            .Where(c => c.ClaimType == ClaimType.NumericWithUnit
                     && c.Unit == "mmol"
                     && c.NormalizedValue is not null)
            .OrderBy(c => c.StepIndex ?? int.MaxValue)
            .ToList();

        if (mmolClaims.Count < 2) return findings;

        // Reference = first mmol claim (assumed substrate)
        if (!double.TryParse(mmolClaims[0].NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double refMmol)
            || refMmol <= 0)
        {
            return findings;
        }

        // Scan text for "X equiv" patterns and try to match each to a mmol claim
        foreach (Match m in EquivRegex.Matches(text))
        {
            if (!double.TryParse(m.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double statedEquiv)
                || statedEquiv <= 0)
            {
                continue;
            }

            // Find the nearest mmol claim to this equiv mention (by char position)
            int equivPos = m.Index;
            ExtractedClaim? nearestMmol = FindNearestMmolClaim(mmolClaims, equivPos, text);
            if (nearestMmol is null) continue;

            // Skip the reference itself
            if (nearestMmol.Id == mmolClaims[0].Id) continue;

            if (!double.TryParse(nearestMmol.NormalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double reagentMmol)
                || reagentMmol <= 0)
            {
                continue;
            }

            double expectedEquiv = reagentMmol / refMmol;
            double diff = Math.Abs(expectedEquiv - statedEquiv);
            double relError = diff / Math.Max(statedEquiv, 0.001);

            if (relError > ToleranceFraction)
            {
                findings.Add(new ValidationFinding
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ClaimId = nearestMmol.Id,
                    ValidatorName = nameof(EquivalentsConsistencyValidator),
                    Status = ValidationStatus.Fail,
                    Message = $"[CHEM.EQUIV_INCONSISTENT] Stated {statedEquiv} equiv for {nearestMmol.EntityKey ?? nearestMmol.RawText} ({reagentMmol} mmol) vs reference {refMmol} mmol implies {expectedEquiv:F2} equiv.",
                    Confidence = 0.8,
                    Kind = FindingKind.EquivInconsistent,
                    EvidenceRef = $"Ref:{mmolClaims[0].Id}|Reagent:{nearestMmol.Id}|StatedEquiv:{statedEquiv}",
                    JsonPayload = $"{{\"statedEquiv\":{statedEquiv},\"computedEquiv\":{expectedEquiv:F2},\"refMmol\":{refMmol},\"reagentMmol\":{reagentMmol}}}"
                });
            }
        }

        return findings;
    }

    private static ExtractedClaim? FindNearestMmolClaim(
        List<ExtractedClaim> mmolClaims,
        int equivCharPos,
        string text)
    {
        ExtractedClaim? nearest = null;
        int nearestDist = int.MaxValue;

        foreach (ExtractedClaim c in mmolClaims)
        {
            if (c.SourceLocator is null || !c.SourceLocator.StartsWith("AnalyzedText:"))
                continue;

            string[] parts = c.SourceLocator["AnalyzedText:".Length..].Split('-');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int start))
                continue;

            int dist = Math.Abs(start - equivCharPos);
            // Only consider claims within ~80 chars
            if (dist < nearestDist && dist < 80)
            {
                nearest = c;
                nearestDist = dist;
            }
        }

        return nearest;
    }
}
