using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShopForge.Infrastructure.Messaging;

internal sealed class OutboxMessageEntityConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", "messaging");

        builder.Property(message => message.Type).HasMaxLength(100);
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(message => message.Error).HasMaxLength(1000);
        builder.Property(message => message.TraceParent).HasMaxLength(64);

        builder.HasIndex(message => new { message.Status, message.DueAt });
        builder.HasIndex(message => new { message.StoreId, message.Status, message.CreatedAt });
    }
}
