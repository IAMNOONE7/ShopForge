namespace ShopForge.Infrastructure.Secrets;

internal static class SecretStoreOptions
{
    // Not the vault the deployment's own secrets come from (D-076). That one the application only reads, and it
    // holds the database connection string; a merchant rotating their gateway secret needs the application to
    // write, and an application that can write to the vault holding the database password is a worse trade than
    // a second vault (D-152).
    public const string VaultUri = "ProviderSecrets:VaultUri";
}
