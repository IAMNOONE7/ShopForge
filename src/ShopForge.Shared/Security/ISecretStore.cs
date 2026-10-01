namespace ShopForge.Shared.Security;

// Secrets that are looked up while the application runs, rather than read once at startup. The deployment's own
// credentials — the database, the blob account, the platform's Stripe keys — come from Key Vault through
// configuration (D-076) and are fixed for the life of the process. A merchant's gateway secret is not: it is
// added the afternoon they connect their account and rotated without anybody restarting anything (D-139).
//
// A row holds the name; the value lives here and is never read back into an API response or a log line.
public interface ISecretStore
{
    Task<string?> FindAsync(string name, CancellationToken cancellationToken);

    Task SetAsync(string name, string value, CancellationToken cancellationToken);

    Task ForgetAsync(string name, CancellationToken cancellationToken);
}

public static class SecretNames
{
    public const int MaxLength = 120;

    // A name is written into a row by one part of the system and handed to a vault by another, so it is kept to
    // what both agree on: lower-case letters, digits and hyphens. Nothing that could climb out of the namespace
    // it was given, whatever put it in the row.
    public static bool IsValid(string? name) =>
        name is { Length: > 0 and <= MaxLength }
        && name.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-')
        && !name.StartsWith('-')
        && !name.EndsWith('-');

    public static string Required(string? name) =>
        IsValid(name) ? name! : throw new ArgumentException($"'{name}' is not a usable secret name.", nameof(name));
}
