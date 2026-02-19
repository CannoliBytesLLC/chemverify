using System.Text.RegularExpressions;
using ChemVerify.Abstractions;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Models;
using ChemVerify.Abstractions.Validation;

namespace ChemVerify.Core.Validators;

[ValidatorMetadata(
    Id = "DOI_FORMAT",
    Kind = FindingKind.MissingEvidence,
    DefaultWeight = 0.15,
    DefaultSeverity = Severity.Low,
    Description = "Validates that DOI strings conform to the 10.xxxx/suffix format and do not exceed maximum length.")]
public class DoiFormatValidator : ValidatorBase
{
    private static readonly Regex DoiRegex = new(
        @"^10\.\d{4,9}/[-._;()/:A-Z0-9]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MaxDoiLength = 256;

    protected override IReadOnlyList<ValidationFinding> ExecuteValidation(
        Guid runId,
        IReadOnlyList<ExtractedClaim> claims,
        AiRun run)
    {
        List<ValidationFinding> findings = new();

        foreach (ExtractedClaim claim in claims)
        {
            if (claim.ClaimType != ClaimType.CitationDoi)
            {
                continue;
            }

            string doi = claim.NormalizedValue ?? claim.RawText;
            bool isValid = DoiRegex.IsMatch(doi) && doi.Length <= MaxDoiLength;

            findings.Add(BuildFinding(
                runId,
                status: isValid ? ValidationStatus.Pass : ValidationStatus.Fail,
                message: isValid ? "DOI format is valid." : "DOI format is invalid or exceeds maximum length.",
                confidence: isValid ? 1.0 : 0.9,
                claimId: claim.Id,
                evidenceRef: FormatClaimRef(claim.Id)));
        }

        return findings;
    }
}

