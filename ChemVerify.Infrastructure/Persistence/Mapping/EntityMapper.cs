using ChemVerify.Abstractions.Models;
using ChemVerify.Infrastructure.Persistence.Entities;

namespace ChemVerify.Infrastructure.Persistence.Mapping;

internal static class EntityMapper
{
    internal static AiRunEntity ToEntity(AiRun run)
    {
        return new AiRunEntity
        {
            Id = run.Id,
            CreatedUtc = run.CreatedUtc,
            Status = run.Status,
            Mode = run.Mode,
            UserId = run.UserId,
            ModelName = run.ModelName,
            PolicyProfile = run.PolicyProfile,
            ConnectorName = run.ConnectorName,
            ModelVersion = run.ModelVersion,
            ParametersJson = run.ParametersJson,
            Prompt = run.Prompt,
            InputText = run.InputText,
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
            Mode = entity.Mode,
            UserId = entity.UserId,
            ModelName = entity.ModelName,
            PolicyProfile = entity.PolicyProfile,
            ConnectorName = entity.ConnectorName,
            ModelVersion = entity.ModelVersion,
            ParametersJson = entity.ParametersJson,
            Prompt = entity.Prompt,
            InputText = entity.InputText,
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
            JsonPayload = claim.JsonPayload,
            EntityKey = claim.EntityKey,
            StepIndex = claim.StepIndex
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
            JsonPayload = entity.JsonPayload,
            EntityKey = entity.EntityKey,
            StepIndex = entity.StepIndex
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
            Kind = finding.Kind,
            JsonPayload = finding.JsonPayload,
            EvidenceStartOffset = finding.EvidenceStartOffset,
            EvidenceEndOffset = finding.EvidenceEndOffset,
            EvidenceStepIndex = finding.EvidenceStepIndex,
            EvidenceEntityKey = finding.EvidenceEntityKey,
            EvidenceSnippet = finding.EvidenceSnippet
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
            Kind = entity.Kind,
            JsonPayload = entity.JsonPayload,
            EvidenceStartOffset = entity.EvidenceStartOffset,
            EvidenceEndOffset = entity.EvidenceEndOffset,
            EvidenceStepIndex = entity.EvidenceStepIndex,
            EvidenceEntityKey = entity.EvidenceEntityKey,
            EvidenceSnippet = entity.EvidenceSnippet
        };
    }
}

