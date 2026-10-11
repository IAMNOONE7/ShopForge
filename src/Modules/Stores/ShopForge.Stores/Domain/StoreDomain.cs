using System.Security.Cryptography;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Domain;

// A host name a store answers on. The platform's own subdomain is given to a store when it is created and is
// trusted because the platform owns the zone it is in; anything a merchant types has to be proved before it is
// served, or a store could claim a name it does not own (D-124, D-189).
//
// A claim nobody proves does not hold the name for ever. The name is unique across the platform, so an unproved
// claim left lying there is a name its real owner cannot add — which is the same denial whether somebody meant
// it or simply gave up halfway (D-189).
internal sealed class StoreDomain : IStoreOwned
{
    // Where the proof goes, and what it looks like.
    public const string ChallengePrefix = "_shopforge-challenge";

    // How long a claim holds a name before it goes back. Long enough for a merchant to reach whoever runs their
    // DNS and for the record to spread; short enough that a name is not parked for a quarter.
    public static readonly TimeSpan ClaimHoldsFor = TimeSpan.FromDays(14);

    private StoreDomain()
    {
    }

    private StoreDomain(Guid storeId, string hostName, bool isPrimary, DateTimeOffset? verifiedAt, string? verificationToken, DateTimeOffset claimedAt)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        HostName = HostNames.Normalize(hostName)
            ?? throw new ArgumentException($"'{hostName}' is not a valid host name.", nameof(hostName));
        IsPrimary = isPrimary;
        VerifiedAt = verifiedAt;
        VerificationToken = verificationToken;
        ClaimedAt = claimedAt;
    }

    // Composed by `PlatformHosts` and nowhere else, which is what stops this being handed a name the platform
    // does not own. It is internal to the domain for the same reason (D-189).
    internal static StoreDomain OwnedByThePlatform(Guid storeId, string hostName, bool isPrimary, DateTimeOffset now) =>
        new(storeId, hostName, isPrimary, now, verificationToken: null, now);

    internal static StoreDomain ClaimedByTheStore(Guid storeId, string hostName, DateTimeOffset now) =>
        new(storeId, hostName, isPrimary: false, verifiedAt: null, Token(), now);

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string HostName { get; private set; } = null!;

    public bool IsPrimary { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    // Kept after verification so the record can stay in DNS and the proof can be checked again later.
    public string? VerificationToken { get; private set; }

    // When the store asked for this name. A proved name keeps it as history; an unproved one is counting down.
    public DateTimeOffset ClaimedAt { get; private set; }

    public bool IsVerified => VerifiedAt is not null;

    // Null once it is proved: a name the store owns is not holding anybody else up.
    public DateTimeOffset? ClaimExpiresAt => IsVerified ? null : ClaimedAt + ClaimHoldsFor;

    public bool ClaimHasLapsed(DateTimeOffset now) => ClaimExpiresAt is { } expires && expires <= now;

    // What the merchant is told to put in DNS: a TXT record at a name nothing else uses.
    public string ChallengeName => $"{ChallengePrefix}.{HostName}";

    public void Verify(DateTimeOffset now) => VerifiedAt ??= now;

    public void MakePrimary() => IsPrimary = true;

    public void GiveUpPrimary() => IsPrimary = false;

    private static string Token() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
