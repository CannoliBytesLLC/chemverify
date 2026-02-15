using System.Text.RegularExpressions;
using Aegis.Core.Enums;
using Aegis.Core.Interfaces;
using Aegis.Core.Models;

namespace Aegis.Infrastructure.Validators;

public class DoiFormatValidator : IValidator
{
    private static readonly Regex DoiRegex = new(
        @"^10\.\d{4,9}/[-._;()/:A-Z0-9]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MaxDoiLength = 256;

    public IReadOnlyList<ValidationFinding> Validate(
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

            findings.Add(new ValidationFinding
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ClaimId = claim.Id,
                ValidatorName = nameof(DoiFormatValidator),
                Status = isValid ? ValidationStatus.Pass : ValidationStatus.Fail,
                Message = isValid ? "DOI format is valid." : "DOI format is invalid or exceeds maximum length.",
                Confidence = isValid ? 1.0 : 0.9,
                EvidenceRef = $"Claim:{claim.Id}"
            });
        }

        return findings;
    }
}
