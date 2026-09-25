using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Stores.Domain;

namespace ShopForge.Stores.Persistence;

internal sealed class TenantEntityConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", StoresModule.Schema);

        builder.Property(tenant => tenant.Name).HasMaxLength(200);
        builder.Property(tenant => tenant.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Plan>().WithMany().HasForeignKey(tenant => tenant.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PlanEntityConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans", StoresModule.Schema, table =>
            table.HasCheckConstraint("ck_plans_caps", "(max_stores IS NULL OR max_stores >= 0) AND (max_products IS NULL OR max_products >= 0)"));

        builder.Property(plan => plan.Code).HasMaxLength(40);
        builder.Property(plan => plan.Name).HasMaxLength(100);

        builder.HasIndex(plan => plan.Code).IsUnique();

        // One plan is the one a tenant falls back to; two would make "the default" a question (D-108).
        builder.HasIndex(plan => plan.IsDefault).IsUnique().HasFilter("is_default");
    }
}
