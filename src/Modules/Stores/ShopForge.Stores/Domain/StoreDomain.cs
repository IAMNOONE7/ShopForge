using System.Security.Cryptography;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Stores.Domain;

// A host name a store answers on. The platform's own subdomain is given to a store when it is created and is
// trusted because the platform owns it; anything a merchant types has to be proved before it is served, or a
// store could claim a name it does not own (D-124).
internal sealed class StoreDomain : IStoreOwned
{
    // Where the proof goes, and what it looks like.
    public const string ChallengePrefix = "_shopforge-challenge";

    private StoreDomain()
    {
    }

    internal StoreDomain(Guid storeId, string hostName, bool isPrimary, DateTimeOffset? verifiedAt, string? verificationToken)
    {
        Id = Guid.CreateVersion7();
        StoreId = storeId;
        HostName = HostNames.Normalize(hostName)
            ?? throw new ArgumentException($"'{hostName}' is not a valid host name.", nameof(hostName));
        IsPrimary = isPrimary;
        VerifiedAt = verifiedAt;
        VerificationToken = verificationToken;
    }

    public static StoreDomain OwnedByThePlatform(Guid storeId, string hostName, bool isPrimary, DateTimeOffset now) =>
        new(storeId, hostName, isPrimary, now, verificationToken: null);

    public static StoreDomain ClaimedByTheStore(Guid storeId, string hostName) =>
        new(storeId, hostName, isPrimary: false, verifiedAt: null, Token());

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string HostName { get; private set; } = null!;

    public bool IsPrimary { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    // Kept after verification so the record can stay in DNS and the proof can be checked again later.
    public string? VerificationToken { get; private set; }

    public bool IsVerified => VerifiedAt is not null;

    // What the merchant is told to put in DNS: a TXT record at a name nothing else uses.
    public string ChallengeName => $"{ChallengePrefix}.{HostName}";

    public void Verify(DateTimeOffset now) => VerifiedAt ??= now;

    public void MakePrimary() => IsPrimary = true;

    public void GiveUpPrimary() => IsPrimary = false;

    private static string Token() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
