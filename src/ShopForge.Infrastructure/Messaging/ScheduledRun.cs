using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ShopForge.Infrastructure.Messaging;

// When a pass is next due, and which replica took the last one — written by `RunSchedule` in one statement, read
// by an operator wondering whether the nightly work ran (D-202).
internal sealed class ScheduledRun
{
    public const int MaxNameLength = 100;
    public const int MaxReplicaLength = 200;

    private ScheduledRun()
    {
    }

    public string Name { get; private set; } = null!;

    public DateTimeOffset DueAt { get; private set; }

    public string? ClaimedBy { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }
}

internal sealed class ScheduledRunEntityConfiguration : IEntityTypeConfiguration<ScheduledRun>
{
    public void Configure(EntityTypeBuilder<ScheduledRun> builder)
    {
        builder.ToTable("scheduled_runs", "messaging");
        builder.HasKey(run => run.Name);

        builder.Property(run => run.Name).HasMaxLength(ScheduledRun.MaxNameLength);
        builder.Property(run => run.ClaimedBy).HasMaxLength(ScheduledRun.MaxReplicaLength);
    }
}
