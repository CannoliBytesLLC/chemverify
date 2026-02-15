using Aegis.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Infrastructure.Persistence;

public class AegisDbContext : DbContext
{
    public AegisDbContext(DbContextOptions<AegisDbContext> options) : base(options) { }

    public DbSet<AiRunEntity> AiRuns => Set<AiRunEntity>();
    public DbSet<ExtractedClaimEntity> ExtractedClaims => Set<ExtractedClaimEntity>();
    public DbSet<ValidationFindingEntity> ValidationFindings => Set<ValidationFindingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AegisDbContext).Assembly);
    }
}
