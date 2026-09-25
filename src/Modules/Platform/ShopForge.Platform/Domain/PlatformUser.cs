namespace ShopForge.Platform.Domain;

// Whoever runs ShopForge. Deliberately outside tenancy: a platform user belongs to no company, which is the whole
// point of being able to see all of them (D-103).
internal sealed class PlatformUser
{
    private PlatformUser()
    {
    }

    public PlatformUser(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        Id = Guid.CreateVersion7();
        Email = NormalizeEmail(email);
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public void SetPasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        PasswordHash = passwordHash;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
