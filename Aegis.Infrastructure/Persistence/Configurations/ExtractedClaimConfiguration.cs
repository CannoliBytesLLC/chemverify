using Aegis.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aegis.Infrastructure.Persistence.Configurations;

public class ExtractedClaimConfiguration : IEntityTypeConfiguration<ExtractedClaimEntity>
{
    public void Configure(EntityTypeBuilder<ExtractedClaimEntity> builder)
    {
        builder.ToTable("ExtractedClaims");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ClaimType).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(c => c.RawText).IsRequired();
        builder.Property(c => c.NormalizedValue).HasMaxLength(500);
        builder.Property(c => c.Unit).HasMaxLength(50);
        builder.Property(c => c.SourceLocator).HasMaxLength(500);
        builder.Property(c => c.JsonPayload);
    }
}
