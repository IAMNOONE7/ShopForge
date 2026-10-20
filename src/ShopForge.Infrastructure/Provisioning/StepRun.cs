using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Provisioning;

// One run of one step: what the step may read of its operation, and the one thing it may write before it has finished,
// which is a secret (D-210).
internal sealed class StepRun(ProvisioningOperation operation, ProvisioningStep step, DbContext dbContext, ISecretStore secrets, TimeProvider clock)
    : IStepContext
{
    public Guid OperationId => operation.Id;

    public Guid StoreId => operation.StoreId;

    public string Kind => operation.Kind;

    public string Step => step.Name;

    public string? Subject => operation.Subject;

    public int Attempts => step.Attempts;

    public int Checks => step.Checks;

    public DateTimeOffset? WaitingSince => step.WaitingSince;

    public string? ResourceId => step.ResourceId;

    public bool Created => step.Created;

    // Derived from the store, the kind, the step and the subject, and nothing that changes between runs.
    public string StableName
    {
        get
        {
            var name = $"shopforge-{operation.StoreId:N}-{Plain(operation.Kind)}-{Plain(step.Name)}";

            return operation.Subject is null
                ? name
                : $"{name}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(operation.Subject)))[..12]}";
        }
    }

    public T? Input<T>() => operation.Input is null ? default : JsonSerializer.Deserialize<T>(operation.Input, Operations.Json);

    public T? Output<T>(string name) =>
        operation.Steps.SingleOrDefault(candidate => candidate.Name == name)?.Data is { } data
            ? JsonSerializer.Deserialize<T>(data, Operations.Json)
            : default;

    public bool HoldsSecretFor(string resourceId) => step.SecretFor == resourceId;

    // The vault first, then the row that says whose secret it is. A crash between the two leaves a secret nobody
    // claims, which the next run's replacement writes over; never a row claiming a secret that is not there.
    public async Task KeepSecretAsync(string resourceId, string secret, CancellationToken cancellationToken)
    {
        await secrets.SetAsync(step.SecretName, secret, cancellationToken);
        step.KeepSecretFor(resourceId);
        operation.Touch(clock.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<string?> SecretAsync(CancellationToken cancellationToken) =>
        step.SecretFor is null ? Task.FromResult<string?>(null) : secrets.FindAsync(step.SecretName, cancellationToken);

    private static string Plain(string value) =>
        new([.. value.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')]);
}
