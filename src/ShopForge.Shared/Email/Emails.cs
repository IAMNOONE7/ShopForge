namespace ShopForge.Shared.Email;

public static class Emails
{
    public const int MaxLength = 254;

    // Deliberately loose: the only proof that an address works is a message arriving at it.
    public static bool IsValid(string? email) =>
        email is { Length: <= MaxLength }
        && email.Trim().Length == email.Length
        && email.Count(character => character == '@') == 1
        && email.Split('@') is [{ Length: > 0 }, { Length: > 2 } domain]
        && domain.Contains('.');
}
