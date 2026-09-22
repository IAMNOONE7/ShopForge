using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Persistence;

public sealed class ShopForgeDbContext(
    DbContextOptions<ShopForgeDbContext> options,
    IStoreContext storeContext,
    EntityConfigurationAssemblies configurationAssemblies) : DbContext(options)
{
    private Guid? CurrentStoreId => storeContext.StoreId;

    private Guid? CurrentTenantId => storeContext.TenantId;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureChangesStayWithinCurrentStore();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureChangesStayWithinCurrentStore();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var assembly in configurationAssemblies.Assemblies.Append(typeof(ShopForgeDbContext).Assembly))
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }

        // Every Guid key in ShopForge is generated in code (Guid v7). Without this, EF treats a set key on a new child
        // added to a tracked parent as an existing row and issues an UPDATE instead of an INSERT.
        var keyProperties = modelBuilder.Model.GetEntityTypes()
            .Select(type => type.FindPrimaryKey())
            .OfType<IMutableKey>()
            .SelectMany(key => key.Properties)
            .Where(property => property.ClrType == typeof(Guid));

        foreach (var property in keyProperties)
        {
            property.ValueGenerated = ValueGenerated.Never;
        }

        foreach (var clrType in modelBuilder.Model.GetEntityTypes().Where(type => !type.IsOwned()).Select(type => type.ClrType))
        {
            if (typeof(IStoreOwned).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasQueryFilter(
                    TenancyFilters.Store, OwnershipFilter(clrType, nameof(IStoreOwned.StoreId), nameof(CurrentStoreId)));
            }

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasQueryFilter(
                    TenancyFilters.Tenant, OwnershipFilter(clrType, nameof(ITenantOwned.TenantId), nameof(CurrentTenantId)));
            }
        }
    }

    // Builds entity => entity.<Owner> == this.<CurrentOwner>. EF Core evaluates the context property
    // per query, so every request is filtered by its own store; with no store resolved nothing matches.
    private LambdaExpression OwnershipFilter(Type entityType, string ownerProperty, string currentOwnerProperty)
    {
        var entity = Expression.Parameter(entityType, "entity");
        var owner = Expression.Convert(Expression.Property(entity, ownerProperty), typeof(Guid?));
        var currentOwner = Expression.Property(Expression.Constant(this), currentOwnerProperty);

        return Expression.Lambda(Expression.Equal(owner, currentOwner), entity);
    }

    private void EnsureChangesStayWithinCurrentStore()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            if (entry.Entity is IStoreOwned && !BelongsTo(entry, nameof(IStoreOwned.StoreId), CurrentStoreId))
            {
                throw new TenancyViolationException($"{entry.Metadata.DisplayName()} does not belong to the current store.");
            }

            if (entry.Entity is ITenantOwned && !BelongsTo(entry, nameof(ITenantOwned.TenantId), CurrentTenantId))
            {
                throw new TenancyViolationException($"{entry.Metadata.DisplayName()} does not belong to the current tenant.");
            }
        }
    }

    private static bool BelongsTo(EntityEntry entry, string ownerProperty, Guid? currentOwner)
    {
        var property = entry.Property(ownerProperty);
        var owner = (Guid)property.CurrentValue!;
        var originalOwner = entry.State == EntityState.Added ? owner : (Guid)property.OriginalValue!;

        return currentOwner is not null && owner == currentOwner && originalOwner == currentOwner;
    }
}
