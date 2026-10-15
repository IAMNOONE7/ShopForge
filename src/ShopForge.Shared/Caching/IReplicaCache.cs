namespace ShopForge.Shared.Caching;

// What one replica keeps of something every replica reads — the store behind a host name, a merchant's secret —
// so that the hot path does not ask the database or the vault each time. What is kept is kept only while the
// replica can hear the others say it changed: forgetting a key here forgets it on every replica, and a replica
// that cannot hear keeps nothing (D-202).
public interface IReplicaCache
{
    // What is kept under the key, or what `load` finds. A value is kept for next time only when `keep` says it is
    // worth keeping and nothing could have been missed while it was being read: the replica heard the channel
    // throughout, and was told to forget nothing in the meantime.
    Task<T> GetAsync<T>(string key, TimeSpan heldFor, Func<CancellationToken, Task<T>> load, Func<T, bool> keep, CancellationToken cancellationToken);

    // Called once the change behind the keys is saved: this replica forgets them at once, the others as soon as
    // the database passes the word on.
    Task ForgetAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}
