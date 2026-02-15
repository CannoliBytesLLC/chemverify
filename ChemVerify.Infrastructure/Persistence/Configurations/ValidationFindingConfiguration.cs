using ChemVerify.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChemVerify.Infrastructure.Persistence.Configurations;

public class ValidationFindingConfiguration : IEntityTypeConfiguration<ValidationFindingEntity>
{
    public void Configure(EntityTypeBuilder<ValidationFindingEntity> builder)
    {
        builder.ToTable("ValidationFindings");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.ValidatorName).IsRequired().HasMaxLength(200);
        builder.Property(f => f.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Message).IsRequired();
        builder.Property(f => f.EvidenceRef).HasMaxLength(500);
        builder.Property(f => f.Kind).HasMaxLength(50);

        builder.HasOne(f => f.Claim)
            .WithMany()
            .HasForeignKey(f => f.ClaimId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

