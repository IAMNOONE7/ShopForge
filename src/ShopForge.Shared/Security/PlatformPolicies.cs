namespace ShopForge.Shared.Security;

// Administering ShopForge itself is a third kind of user, next to a company's staff and its customers. Its own
// scheme and its own cookie: neither of the other two opens these doors (D-103).
public static class PlatformPolicies
{
    public const string Scheme = "Platform";
    public const string PlatformUser = "PlatformUser";
}
