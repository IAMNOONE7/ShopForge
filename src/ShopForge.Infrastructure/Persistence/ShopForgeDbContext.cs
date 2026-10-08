using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using ShopForge.Infrastructure.Auditing;
using ShopForge.Infrastructure.Email;
using ShopForge.Infrastructure.Messaging;
using ShopForge.Shared.Catalog;
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
        EnsureTheRecordIsOnlyAddedTo();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureChangesStayWithinCurrentStore();
        EnsureTheRecordIsOnlyAddedTo();
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

            // A retired row is hidden the same way another store's is: once, here, for everything that can be
            // retired. The catalogue, the search, the feeds and the sitemap then know nothing about archiving
            // and so cannot forget it (D-180).
            if (typeof(IArchivable).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasQueryFilter(TenancyFilters.Archived, NotArchivedFilter(clrType));
            }
        }

        // The outbox owns its rows loosely (D-111), so it cannot join the loop above: a message that belongs to no
        // store must not surface in a store's admin, which the null check says outright rather than leaving to how
        // a null parameter compares in SQL. Whoever wants the rest lifts the filter and says which ones it wants.
        modelBuilder.Entity<OutboxMessage>().HasQueryFilter(
            TenancyFilters.Store,
            message => message.StoreId != null && message.StoreId == CurrentStoreId);

        // An address a store may not write to is the store's own business, and a bounce on mail that belonged to
        // no store belongs to none either (D-123).
        modelBuilder.Entity<SuppressedAddress>().HasQueryFilter(
            TenancyFilters.Store,
            address => address.StoreId != null && address.StoreId == CurrentStoreId);

        // The record belongs to a company loosely for the same reason (D-116): the platform's own actions belong to
        // no company, and what it does to one is written from outside that company's scope. A company reads its own.
        modelBuilder.Entity<AuditEntry>().HasQueryFilter(
            TenancyFilters.Tenant,
            entry => entry.TenantId != null && entry.TenantId == CurrentTenantId);
    }

    // Builds entity => entity.<Owner> == this.<CurrentOwner>. EF Core evaluates the context property
    // per query, so every request is filtered by its own store; with no store resolved nothing matches.
    private static LambdaExpression NotArchivedFilter(Type entityType)
    {
        var row = Expression.Parameter(entityType, "row");

        return Expression.Lambda(
            Expression.Equal(
                Expression.Property(row, nameof(IArchivable.ArchivedAt)),
                Expression.Constant(null, typeof(DateTimeOffset?))),
            row);
    }

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

            // An outbox message may name no owner at all, but one it does name is held to just as tightly (D-111).
            if (entry.Entity is OutboxMessage message)
            {
                if (message.StoreId is not null && !BelongsTo(entry, nameof(OutboxMessage.StoreId), CurrentStoreId))
                {
                    throw new TenancyViolationException("The message does not belong to the current store.");
                }

                if (message.TenantId is not null && !BelongsTo(entry, nameof(OutboxMessage.TenantId), CurrentTenantId))
                {
                    throw new TenancyViolationException("The message does not belong to the current tenant.");
                }
            }
        }
    }

    // What was done cannot be undone in the telling (D-116). Endpoints could simply never write one, but a record
    // that depends on everybody remembering that is not a record.
    private void EnsureTheRecordIsOnlyAddedTo()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("An audit entry is a record of what happened; it is never changed or removed.");
            }
        }
    }

    private static bool BelongsTo(EntityEntry entry, string ownerProperty, Guid? currentOwner)
    {
        var property = entry.Property(ownerProperty);
        var owner = (Guid?)property.CurrentValue;
        var originalOwner = entry.State == EntityState.Added ? owner : (Guid?)property.OriginalValue;

        return currentOwner is not null && owner == currentOwner && originalOwner == currentOwner;
    }
}
