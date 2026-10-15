using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using ShopForge.Shared.Caching;

namespace ShopForge.Infrastructure.Caching;

// One per replica. Everything kept belongs to the current generation: when the replica stops hearing the channel,
// the generation ends and all of it goes at once, because what changed in the meantime is unknown. While it cannot
// hear, it keeps nothing and every lookup goes to the source (D-202).
//
// A lookup that read the database just before a change, and finishes just after the word to forget arrived, would
// keep the old answer for minutes. So every forgetting is counted, and an answer that has been put in is taken out
// again if the count moved at any time since the lookup began.
internal sealed class ReplicaCache(IMemoryCache cache, CacheChannel channel) : IReplicaCache
{
    private CancellationTokenSource _generation = new();
    private volatile bool _hearing;
    private long _forgotten;

    public bool IsHearing => _hearing;

    public async Task<T> GetAsync<T>(
        string key, TimeSpan heldFor, Func<CancellationToken, Task<T>> load, Func<T, bool> keep, CancellationToken cancellationToken)
    {
        // Read in this order: an answer read before the replica went deaf belongs to the generation that ended.
        var generation = Volatile.Read(ref _generation).Token;
        var hearing = _hearing;
        var forgotten = Interlocked.Read(ref _forgotten);

        if (hearing && cache.TryGetValue(key, out T? held))
        {
            return held!;
        }

        var loaded = await load(cancellationToken);

        if (!hearing || !keep(loaded))
        {
            return loaded;
        }

        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = heldFor };
        options.ExpirationTokens.Add(new CancellationChangeToken(generation));
        cache.Set(key, loaded, options);

        if (Interlocked.Read(ref _forgotten) != forgotten)
        {
            cache.Remove(key);
        }

        return loaded;
    }

    public async Task ForgetAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        Forget(keys);
        await channel.SayAsync(keys);
    }

    // What another replica, or this one, said has changed. Counted before anything is removed, so a lookup that
    // puts its answer in between the two sees the count move and takes it out again.
    public void Forget(IEnumerable<string> keys)
    {
        Interlocked.Increment(ref _forgotten);

        foreach (var key in keys)
        {
            cache.Remove(key);
        }
    }

    // The listener is in place, so what is read from now on will be told about when it changes.
    public void Hearing() => _hearing = true;

    // The old source is cancelled and not disposed: an entry being made at this moment may still be registering
    // against its token, and a cancelled token is what makes that entry expire at once.
    public void Deaf()
    {
        _hearing = false;
        Interlocked.Exchange(ref _generation, new CancellationTokenSource()).Cancel();
    }
}
