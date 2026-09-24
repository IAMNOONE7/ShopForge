using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Email;

namespace ShopForge.Customers.Persistence;

internal sealed class CustomerIdentityEntityConfiguration : IEntityTypeConfiguration<CustomerIdentity>
{
    public void Configure(EntityTypeBuilder<CustomerIdentity> builder)
    {
        builder.ToTable("customer_identities", CustomersModule.Schema);

        builder.Property(identity => identity.Email).HasMaxLength(Emails.MaxLength);

        builder.HasIndex(identity => new { identity.TenantId, identity.Email }).IsUnique();
    }
}

internal sealed class StoreCustomerEntityConfiguration : IEntityTypeConfiguration<StoreCustomer>
{
    public void Configure(EntityTypeBuilder<StoreCustomer> builder)
    {
        builder.ToTable("store_customers", CustomersModule.Schema);

        builder.Property(customer => customer.FirstName).HasMaxLength(StoreCustomer.MaxNameLength);
        builder.Property(customer => customer.LastName).HasMaxLength(StoreCustomer.MaxNameLength);
        builder.Property(customer => customer.Phone).HasMaxLength(30);
        builder.Property(customer => customer.PasswordHash).HasMaxLength(200);
        builder.Ignore(customer => customer.FullName);

        builder.HasOne<CustomerIdentity>().WithMany().HasForeignKey(customer => customer.CustomerIdentityId);
        builder.HasIndex(customer => new { customer.StoreId, customer.CustomerIdentityId }).IsUnique();
    }
}

internal sealed class PendingRegistrationEntityConfiguration : IEntityTypeConfiguration<PendingRegistration>
{
    public void Configure(EntityTypeBuilder<PendingRegistration> builder)
    {
        builder.ToTable("pending_registrations", CustomersModule.Schema);

        builder.Property(registration => registration.FirstName).HasMaxLength(StoreCustomer.MaxNameLength);
        builder.Property(registration => registration.LastName).HasMaxLength(StoreCustomer.MaxNameLength);
        builder.Property(registration => registration.Phone).HasMaxLength(30);
        builder.Property(registration => registration.PasswordHash).HasMaxLength(200);

        builder.HasOne<CustomerIdentity>().WithMany().HasForeignKey(registration => registration.CustomerIdentityId);
        builder.HasIndex(registration => new { registration.StoreId, registration.CustomerIdentityId }).IsUnique();
    }
}

internal sealed class CustomerTokenEntityConfiguration : IEntityTypeConfiguration<CustomerToken>
{
    public void Configure(EntityTypeBuilder<CustomerToken> builder)
    {
        builder.ToTable("customer_tokens", CustomersModule.Schema);

        builder.Property(token => token.TokenHash).HasMaxLength(64).IsFixedLength();
        builder.Property(token => token.Purpose).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<CustomerIdentity>().WithMany().HasForeignKey(token => token.CustomerIdentityId);
        builder.HasIndex(token => token.TokenHash).IsUnique();
    }
}

internal sealed class WishlistItemEntityConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.ToTable("wishlist_items", CustomersModule.Schema);

        builder.HasOne<StoreCustomer>().WithMany().HasForeignKey(item => item.StoreCustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.StoreCustomerId, item.StoreProductId }).IsUnique();
    }
}
