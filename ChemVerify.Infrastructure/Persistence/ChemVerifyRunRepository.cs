using ChemVerify.Abstractions.Models;
using ChemVerify.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ChemVerify.Infrastructure.Persistence;

public class ChemVerifyRunRepository
{
    private readonly ChemVerifyDbContext _db;

    public ChemVerifyRunRepository(ChemVerifyDbContext db)
    {
        _db = db;
    }

    public async Task SaveRunAsync(
        AiRun run,
        IReadOnlyList<ExtractedClaim> claims,
        IReadOnlyList<ValidationFinding> findings,
        CancellationToken ct)
    {
        Entities.AiRunEntity runEntity = EntityMapper.ToEntity(run);
        _db.AiRuns.Add(runEntity);

        foreach (ExtractedClaim claim in claims)
        {
            _db.ExtractedClaims.Add(EntityMapper.ToEntity(claim));
        }

        foreach (ValidationFinding finding in findings)
        {
            _db.ValidationFindings.Add(EntityMapper.ToEntity(finding));
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<AuditArtifact?> GetAuditArtifactAsync(Guid runId, CancellationToken ct)
    {
        Entities.AiRunEntity? runEntity = await _db.AiRuns
            .AsNoTracking()
            .Include(r => r.Claims)
            .Include(r => r.Findings)
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        if (runEntity is null)
        {
            return null;
        }

        AiRun run = EntityMapper.ToDomain(runEntity);
        List<ExtractedClaim> claims = runEntity.Claims.Select(EntityMapper.ToDomain).ToList();
        List<ValidationFinding> findings = runEntity.Findings.Select(EntityMapper.ToDomain).ToList();

        return new AuditArtifact
        {
            RunId = run.Id,
            Run = run,
            Claims = claims,
            Findings = findings,
            ArtifactHash = run.CurrentHash,
            GeneratedUtc = run.CreatedUtc
        };
    }

    public async Task<List<AiRun>> ListRunsAsync(int skip, int take, CancellationToken ct)
    {
        
        
        List<Entities.AiRunEntity> entities = await _db.AiRuns
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return entities.Select(EntityMapper.ToDomain).ToList();
    }
}

