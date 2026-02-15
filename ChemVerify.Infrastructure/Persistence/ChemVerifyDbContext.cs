using ChemVerify.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChemVerify.Infrastructure.Persistence;

public class ChemVerifyDbContext : DbContext
{
    public ChemVerifyDbContext(DbContextOptions<ChemVerifyDbContext> options) : base(options) { }

    public DbSet<AiRunEntity> AiRuns => Set<AiRunEntity>();
    public DbSet<ExtractedClaimEntity> ExtractedClaims => Set<ExtractedClaimEntity>();
    public DbSet<ValidationFindingEntity> ValidationFindings => Set<ValidationFindingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChemVerifyDbContext).Assembly);
    }
}

