using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;

namespace ChemVerify.Core.Validators;

public class MissingTemperatureWhenImpliedValidator : IValidator
{
    private static readonly Regex ImpliedTempRegex = new(
        @"\b(dropwise|exotherm(ic)?|ice\s+bath|cooling\s+bath|reflux(ed|ing)?|cryogenic|maintained\s+at)\b",
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

        bool hasTempClaim = claims.Any(c =>
            c.JsonPayload is not null &&
            c.JsonPayload.Contains("\"temp\"", StringComparison.OrdinalIgnoreCase));

        if (!hasTempClaim)
        {
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
        }

        return findings;
    }
}
