using ChemVerify.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChemVerify.Infrastructure.Persistence.Configurations;

public class AiRunConfiguration : IEntityTypeConfiguration<AiRunEntity>
{
    public void Configure(EntityTypeBuilder<AiRunEntity> builder)
    {
        builder.ToTable("AiRuns");
        builder.HasKey(r => r.Id);

        // Store DateTimeOffset as ISO 8601 string so SQLite can ORDER BY it
        builder.Property(r => r.CreatedUtc)
            .HasConversion(
                v => v.ToString("O"),
                v => DateTimeOffset.Parse(v));

        builder.Property(r => r.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Mode).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.ModelName).IsRequired().HasMaxLength(200);
        builder.Property(r => r.PolicyProfile).HasMaxLength(200);
        builder.Property(r => r.ConnectorName).HasMaxLength(200);
        builder.Property(r => r.ModelVersion).HasMaxLength(100);
        builder.Property(r => r.ParametersJson);
        builder.Property(r => r.Prompt).IsRequired();
        builder.Property(r => r.InputText);
        builder.Property(r => r.Output);
        builder.Property(r => r.CurrentHash).IsRequired().HasMaxLength(128);
        builder.Property(r => r.PreviousHash).HasMaxLength(128);
        builder.Property(r => r.UserId).HasMaxLength(200);

        builder.HasMany(r => r.Claims)
            .WithOne(c => c.Run)
            .HasForeignKey(c => c.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Findings)
            .WithOne(f => f.Run)
            .HasForeignKey(f => f.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

