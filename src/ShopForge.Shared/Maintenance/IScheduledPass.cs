namespace ShopForge.Shared.Maintenance;

// Work that comes round every so often, which any replica can do and only one should do each time. The database
// says when the next pass is due and the first replica to ask once it is due takes it, so a restart does not put
// it off and five replicas do not do it five times (D-202).
public interface IScheduledPass
{
    // The pass's name in the schedule, fixed once a database holds it.
    string Name { get; }

    TimeSpan Period { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
