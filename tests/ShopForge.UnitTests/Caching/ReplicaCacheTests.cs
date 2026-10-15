using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ShopForge.Infrastructure.Caching;

namespace ShopForge.UnitTests.Caching;

// What one replica keeps, and when it refuses to keep it (D-202). Whether the word to forget crosses the database
// is the integration tests' business; here the word arrives exactly when a test says, including in the middle of
// a lookup, which is the case that is hard to catch any other way.
public sealed class ReplicaCacheTests
{
    private const string Key = "stores:host:shop.example.test";

    private static readonly TimeSpan HeldFor = TimeSpan.FromMinutes(5);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_is_read_while_the_replica_can_hear_is_kept()
    {
        var cache = Hearing();
        var reads = 0;

        await cache.GetAsync(Key, HeldFor, _ => Task.FromResult(++reads), _ => true, CancellationToken);
        var second = await cache.GetAsync(Key, HeldFor, _ => Task.FromResult(++reads), _ => true, CancellationToken);

        Assert.Equal((1, 1), (second, reads));
    }

    // A replica starts deaf: until its listener is in place, a change could not reach it.
    [Fact]
    public async Task A_replica_that_cannot_hear_keeps_nothing()
    {
        var cache = Replica();
        var reads = 0;

        await cache.GetAsync(Key, HeldFor, _ => Task.FromResult(++reads), _ => true, CancellationToken);
        await cache.GetAsync(Key, HeldFor, _ => Task.FromResult(++reads), _ => true, CancellationToken);

        Assert.Equal(2, reads);
    }

    // Read before the listener was in place, so a change made meanwhile could not have been heard: once the replica
    // can hear, it reads again rather than trusting it.
    [Fact]
    public async Task What_was_read_before_the_replica_could_hear_is_not_kept_once_it_can()
    {
        var cache = Replica();
        await cache.GetAsync(Key, HeldFor, _ => Task.FromResult("old"), _ => true, CancellationToken);

        cache.Hearing();
        var answer = await cache.GetAsync(Key, HeldFor, _ => Task.FromResult("new"), _ => true, CancellationToken);

        Assert.Equal("new", answer);
    }

    [Fact]
    public async Task What_the_caller_does_not_want_kept_is_not_kept()
    {
        var cache = Hearing();
        var reads = 0;

        await cache.GetAsync(Key, HeldFor, _ => Task.FromResult(++reads), _ => false, CancellationToken);
        await cache.GetAsync(Key, HeldFor, _ => Task.FromResult(++reads), _ => false, CancellationToken);

        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task A_key_this_replica_is_told_to_forget_is_read_again()
    {
        var cache = Hearing();
        await cache.GetAsync(Key, HeldFor, _ => Task.FromResult("old"), _ => true, CancellationToken);

        cache.Forget([Key]);
        var answer = await cache.GetAsync(Key, HeldFor, _ => Task.FromResult("new"), _ => true, CancellationToken);

        Assert.Equal("new", answer);
    }

    // The read saw the database before the change; the word to forget arrived before the read finished. Keeping the
    // answer would keep the old state for the whole holding time.
    [Fact]
    public async Task An_answer_read_while_the_word_to_forget_arrived_is_not_kept()
    {
        var cache = Hearing();

        await cache.GetAsync(Key, HeldFor, _ =>
        {
            cache.Forget([Key]);

            return Task.FromResult("old");
        }, _ => true, CancellationToken);
        var answer = await cache.GetAsync(Key, HeldFor, _ => Task.FromResult("new"), _ => true, CancellationToken);

        Assert.Equal("new", answer);
    }

    // Whatever was kept before the line dropped may have changed while nobody could say so.
    [Fact]
    public async Task Losing_the_channel_drops_everything_kept()
    {
        var cache = Hearing();
        await cache.GetAsync(Key, HeldFor, _ => Task.FromResult("old"), _ => true, CancellationToken);

        cache.Deaf();
        cache.Hearing();
        var answer = await cache.GetAsync(Key, HeldFor, _ => Task.FromResult("new"), _ => true, CancellationToken);

        Assert.Equal("new", answer);
    }

    // The line dropped and came back while the read was under way: the word it would have carried is lost.
    [Fact]
    public async Task An_answer_read_while_the_channel_dropped_and_came_back_is_not_kept()
    {
        var cache = Hearing();

        await cache.GetAsync(Key, HeldFor, _ =>
        {
            cache.Deaf();
            cache.Hearing();

            return Task.FromResult("old");
        }, _ => true, CancellationToken);
        var answer = await cache.GetAsync(Key, HeldFor, _ => Task.FromResult("new"), _ => true, CancellationToken);

        Assert.Equal("new", answer);
    }

    private static ReplicaCache Hearing()
    {
        var cache = Replica();
        cache.Hearing();

        return cache;
    }

    // Nothing here says anything to the database: only what a replica hears is under test.
    private static ReplicaCache Replica() =>
        new(new MemoryCache(new MemoryCacheOptions()), new CacheChannel("Host=127.0.0.1;Port=1;Database=none", NullLogger<CacheChannel>.Instance));
}
