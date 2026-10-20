using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShopForge.Infrastructure.Provisioning;

internal sealed class ProvisioningOperationEntityConfiguration : IEntityTypeConfiguration<ProvisioningOperation>
{
    public void Configure(EntityTypeBuilder<ProvisioningOperation> builder)
    {
        builder.ToTable("operations", "provisioning");

        // A worker's write carries the version it read, so one whose lease ran out, or that crossed a person's retry or
        // cancel, changes nothing and is told (D-130, D-210).
        builder.Property<uint>("Version").HasColumnName("xmin").IsRowVersion();

        builder.Property(operation => operation.Kind).HasMaxLength(ProvisioningOperation.MaxKindLength);
        builder.Property(operation => operation.Subject).HasMaxLength(ProvisioningOperation.MaxSubjectLength);
        builder.Property(operation => operation.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(operation => operation.LeasedBy).HasMaxLength(200);

        builder.HasMany(operation => operation.Steps)
            .WithOne()
            .HasForeignKey(step => step.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(operation => operation.Steps).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_steps");

        // One unfinished operation per store, and per subject across the platform: held by the database, so two
        // requests arriving together cannot both start one (D-210).
        builder.HasIndex(operation => operation.StoreId)
            .IsUnique()
            .HasFilter("finished_at IS NULL")
            .HasDatabaseName("ux_operations_one_unfinished_per_store");
        builder.HasIndex(operation => operation.Subject)
            .IsUnique()
            .HasFilter("finished_at IS NULL AND subject IS NOT NULL")
            .HasDatabaseName("ux_operations_one_unfinished_per_subject");

        builder.HasIndex(operation => operation.NextCheckAt).HasFilter("finished_at IS NULL");
        builder.HasIndex(operation => new { operation.StoreId, operation.CreatedAt });
    }
}

internal sealed class ProvisioningStepEntityConfiguration : IEntityTypeConfiguration<ProvisioningStep>
{
    public void Configure(EntityTypeBuilder<ProvisioningStep> builder)
    {
        builder.ToTable("operation_steps", "provisioning");

        builder.Property(step => step.Name).HasMaxLength(ProvisioningStep.MaxNameLength);
        builder.Property(step => step.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(step => step.ResourceId).HasMaxLength(ProvisioningStep.MaxResourceIdLength);
        builder.Property(step => step.SecretFor).HasMaxLength(ProvisioningStep.MaxResourceIdLength);
        builder.Property(step => step.RequestId).HasMaxLength(ProvisioningStep.MaxRequestIdLength);
        builder.Property(step => step.Note).HasMaxLength(ProvisioningStep.MaxNoteLength);
        builder.Property(step => step.LastError).HasMaxLength(ProvisioningStep.MaxNoteLength);

        builder.HasIndex(step => new { step.OperationId, step.Position }).IsUnique();
    }
}
