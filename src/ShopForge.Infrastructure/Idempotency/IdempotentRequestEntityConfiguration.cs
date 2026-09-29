using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ShopForge.Infrastructure.Idempotency;

internal sealed class IdempotentRequestEntityConfiguration : IEntityTypeConfiguration<IdempotentRequest>
{
    public void Configure(EntityTypeBuilder<IdempotentRequest> builder)
    {
        builder.ToTable("requests", "idempotency");

        builder.Property(request => request.Endpoint).HasMaxLength(IdempotentRequest.MaxEndpointLength);
        builder.Property(request => request.Key).HasMaxLength(Shared.Http.Idempotency.MaxKeyLength);
        builder.Property(request => request.Fingerprint).HasMaxLength(64);
        builder.Property(request => request.ContentType).HasMaxLength(100);
        builder.Property(request => request.Location).HasMaxLength(500);

        // The index is the claim: two identical requests arriving together race for this row, and the one that
        // loses is told the first is in flight rather than doing the work as well.
        builder.HasIndex(request => new { request.StoreId, request.Endpoint, request.Key }).IsUnique();
        builder.HasIndex(request => request.ClaimedAt);
    }
}
