namespace ShopForge.TestRig;

// Where the rig answers a browser, where it finds the API, the database its readings come from, and the operator it
// signs in as to start a run. The stack script sets the operator; nothing here is a real credential.
public sealed record RigOptions(Uri PublicOrigin, Uri ApiOrigin, string Database, string OperatorEmail, string OperatorPassword)
{
    public const string EnvironmentName = "E2E";

    public static RigOptions From(IConfiguration configuration) =>
        new(
            new Uri(Required(configuration, "Rig:PublicOrigin")),
            new Uri(Required(configuration, "Rig:ApiOrigin")),
            Required(configuration, "ConnectionStrings:Readings"),
            configuration["Rig:Operator:Email"] ?? string.Empty,
            configuration["Rig:Operator:Password"] ?? string.Empty);

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"The test rig needs {key}.");
}
