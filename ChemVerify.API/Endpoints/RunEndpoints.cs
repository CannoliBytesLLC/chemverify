using ChemVerify.API.Contracts;
using ChemVerify.Abstractions.Contracts;
using ChemVerify.Abstractions.Enums;
using ChemVerify.Abstractions.Interfaces;
using ChemVerify.Abstractions.Models;
using ChemVerify.Core.Services;
using ChemVerify.Infrastructure.Persistence;

namespace ChemVerify.API.Endpoints;

public static class RunEndpoints
{
    public static void MapRunEndpoints(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/runs")
            .WithTags("Runs");

        group.MapPost("/", CreateRunAsync)
            .WithName("CreateRun")
            .WithDescription("Submit a prompt for AI governance auditing.")
            .Produces<CreateRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetRunAsync)
            .WithName("GetRun")
            .WithDescription("Retrieve the audit artifact for a previous run.")
            .Produces<AuditArtifact>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/report", GetRunReportAsync)
            .WithName("GetRunReport")
            .WithDescription("Retrieve a previous run with the human-readable report included.")
            .Produces<CreateRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/", ListRunsAsync)
            .WithName("ListRuns")
            .WithDescription("List runs with pagination.")
            .Produces<List<RunSummary>>(StatusCodes.Status200OK);

        app.MapPost("/verify", VerifyTextAsync)
            .WithTags("Verify")
            .WithName("VerifyText")
            .WithDescription("Verify pre-existing text without calling a model connector.")
            .Produces<CreateRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> CreateRunAsync(
        CreateRunRequest request,
        IAuditService auditService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Results.BadRequest(new { error = "Prompt is required." });
        }

        Enum.TryParse(request.OutputContract, true, out OutputContract outputContract);

        RunCommand command = new()
        {
            Prompt = request.Prompt,
            UserId = request.UserId,
            ModelName = request.ModelName ?? "mock",
            PolicyProfile = request.PolicyProfile,
            ConnectorName = request.ConnectorName,
            ModelVersion = request.ModelVersion,
            ParametersJson = request.ParametersJson,
            OutputContract = outputContract
        };

        AuditArtifact artifact = await auditService.CreateRunAndAuditAsync(command, ct);

        ReportDto report = ReportBuilder.Build(
            artifact.Run.RiskScore,
            artifact.Claims,
            artifact.Findings);

        CreateRunResponse response = new()
        {
            RunId = artifact.RunId,
            RiskScore = artifact.Run.RiskScore,
            Report = report,
            Artifact = artifact
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> GetRunAsync(
        Guid id,
        ChemVerifyRunRepository repository,
        CancellationToken ct)
    {
        AuditArtifact? artifact = await repository.GetAuditArtifactAsync(id, ct);

        if (artifact is null)
        {
            return Results.NotFound(new { error = "Run not found." });
        }

        return Results.Ok(artifact);
    }

    private static async Task<IResult> GetRunReportAsync(
        Guid id,
        ChemVerifyRunRepository repository,
        CancellationToken ct)
    {
        AuditArtifact? artifact = await repository.GetAuditArtifactAsync(id, ct);

        if (artifact is null)
        {
            return Results.NotFound(new { error = "Run not found." });
        }

        ReportDto report = ReportBuilder.Build(
            artifact.Run.RiskScore,
            artifact.Claims,
            artifact.Findings);

        CreateRunResponse response = new()
        {
            RunId = artifact.RunId,
            RiskScore = artifact.Run.RiskScore,
            Report = report,
            Artifact = artifact
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> VerifyTextAsync(
        VerifyTextRequest request,
        IAuditService auditService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TextToVerify))
        {
            return Results.BadRequest(new { error = "TextToVerify is required." });
        }

        AuditArtifact artifact = await auditService.VerifyTextAsync(
            request.TextToVerify,
            request.PolicyProfile,
            ct);

        ReportDto report = ReportBuilder.Build(
            artifact.Run.RiskScore,
            artifact.Claims,
            artifact.Findings);

        CreateRunResponse response = new()
        {
            RunId = artifact.RunId,
            RiskScore = artifact.Run.RiskScore,
            Report = report,
            Artifact = artifact
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> ListRunsAsync(
        int skip,
        int take,
        ChemVerifyRunRepository repository,
        CancellationToken ct)
    {
        int effectiveTake = Math.Clamp(take, 1, 100);
        int effectiveSkip = Math.Max(0, skip);

        List<AiRun> runs = await repository.ListRunsAsync(effectiveSkip, effectiveTake, ct);

        List<RunSummary> summaries = runs.Select(r => new RunSummary
        {
            Id = r.Id,
            CreatedUtc = r.CreatedUtc,
            Status = r.Status,
            RiskScore = r.RiskScore,
            ModelName = r.ModelName,
            UserId = r.UserId
        }).ToList();

        return Results.Ok(summaries);
    }
}

