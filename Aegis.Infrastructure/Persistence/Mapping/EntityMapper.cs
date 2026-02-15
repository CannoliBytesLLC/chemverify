using Aegis.Core.Models;
using Aegis.Infrastructure.Persistence.Entities;

namespace Aegis.Infrastructure.Persistence.Mapping;

internal static class EntityMapper
{
    internal static AiRunEntity ToEntity(AiRun run)
    {
        return new AiRunEntity
        {
            Id = run.Id,
            CreatedUtc = run.CreatedUtc,
            Status = run.Status,
            UserId = run.UserId,
            ModelName = run.ModelName,
            PolicyProfile = run.PolicyProfile,
            ConnectorName = run.ConnectorName,
            ModelVersion = run.ModelVersion,
            ParametersJson = run.ParametersJson,
            Prompt = run.Prompt,
            Output = run.Output,
            PreviousHash = run.PreviousHash,
            CurrentHash = run.CurrentHash,
            RiskScore = run.RiskScore
        };
    }

    internal static AiRun ToDomain(AiRunEntity entity)
    {
        return new AiRun
        {
            Id = entity.Id,
            CreatedUtc = entity.CreatedUtc,
            Status = entity.Status,
            UserId = entity.UserId,
            ModelName = entity.ModelName,
            PolicyProfile = entity.PolicyProfile,
            ConnectorName = entity.ConnectorName,
            ModelVersion = entity.ModelVersion,
            ParametersJson = entity.ParametersJson,
            Prompt = entity.Prompt,
            Output = entity.Output,
            PreviousHash = entity.PreviousHash,
            CurrentHash = entity.CurrentHash,
            RiskScore = entity.RiskScore
        };
    }

    internal static ExtractedClaimEntity ToEntity(ExtractedClaim claim)
    {
        return new ExtractedClaimEntity
        {
            Id = claim.Id,
            RunId = claim.RunId,
            ClaimType = claim.ClaimType,
            RawText = claim.RawText,
            NormalizedValue = claim.NormalizedValue,
            Unit = claim.Unit,
            SourceLocator = claim.SourceLocator,
            JsonPayload = claim.JsonPayload
        };
    }

    internal static ExtractedClaim ToDomain(ExtractedClaimEntity entity)
    {
        return new ExtractedClaim
        {
            Id = entity.Id,
            RunId = entity.RunId,
            ClaimType = entity.ClaimType,
            RawText = entity.RawText,
            NormalizedValue = entity.NormalizedValue,
            Unit = entity.Unit,
            SourceLocator = entity.SourceLocator,
            JsonPayload = entity.JsonPayload
        };
    }

    internal static ValidationFindingEntity ToEntity(ValidationFinding finding)
    {
        return new ValidationFindingEntity
        {
            Id = finding.Id,
            RunId = finding.RunId,
            ClaimId = finding.ClaimId,
            ValidatorName = finding.ValidatorName,
            Status = finding.Status,
            Message = finding.Message,
            Confidence = finding.Confidence,
            EvidenceRef = finding.EvidenceRef,
            Kind = finding.Kind
        };
    }

    internal static ValidationFinding ToDomain(ValidationFindingEntity entity)
    {
        return new ValidationFinding
        {
            Id = entity.Id,
            RunId = entity.RunId,
            ClaimId = entity.ClaimId,
            ValidatorName = entity.ValidatorName,
            Status = entity.Status,
            Message = entity.Message,
            Confidence = entity.Confidence,
            EvidenceRef = entity.EvidenceRef,
            Kind = entity.Kind
        };
    }
}
