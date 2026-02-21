using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

public class MissingTemperatureWhenImpliedValidator : IValidator
{
    // Thermal-control verbs and phrases that imply a specific temperature is expected.
    // "ice bath" and "reflux" are NOT here — they are now extracted as SymbolicTemperature
    // claims by ReagentRoleExtractor and satisfy the temperature requirement.
    // "maintained at" is excluded because it can appear in non-thermal contexts
    // ("maintained under nitrogen").
    private static readonly Regex ImpliedTempRegex = new(
        @"\b(dropwise|exotherm(ic)?|cooling\s+bath|cryogenic|"
        + @"heated\s+to|cooled\s+to|warmed\s+to|kept\s+at\s+(?!rt\b|room|ambient)|"
        + @"stirred\s+at\s+(?!rt\b|room|ambient))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<ValidationFinding> Validate(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();
        string text = run.GetAnalyzedText();

        if (string.IsNullOrEmpty(text))
        {
            return findings;
        }

        Match impliedMatch = ImpliedTempRegex.Match(text);
        if (!impliedMatch.Success)
        {
            return findings;
        }

        // Check for any temperature claim: numeric (contextKey "temp") or symbolic
        bool hasTempClaim = claims.Any(c =>
            c.ClaimType == ClaimType.SymbolicTemperature
            || (c.JsonPayload is not null
                && c.JsonPayload.Contains("\"temp\"", StringComparison.OrdinalIgnoreCase)));

        if (hasTempClaim)
        {
            return findings;
        }

        findings.Add(new ValidationFinding
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ValidatorName = nameof(MissingTemperatureWhenImpliedValidator),
            Status = ValidationStatus.Fail,
            Message = $"[CHEM.MISSING_TEMPERATURE] Temperature control is implied (e.g., {impliedMatch.Value}) but no temperature was specified.",
            Confidence = 0.85,
            Kind = FindingKind.MissingTemperature,
            EvidenceRef = $"ImpliedTemp@{impliedMatch.Index}"
        });

        return findings;
    }
}
