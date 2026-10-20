using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Provisioning;

// Starts an operation and returns at once; the worker does the rest (D-210). The friendly answer to a store that is
// already running one is here, and the unique indexes are what catch two starts arriving together.
internal sealed class Operations(
    DbContext dbContext,
    IStoreContext storeContext,
    IEnumerable<IOperationKind> kinds,
    IServiceProvider services,
    IAuditLog audit,
    TimeProvider clock) : IProvisioning
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<OperationStart> StartAsync(string kind, string? subject, object? input, CancellationToken cancellationToken)
    {
        var definition = kinds.SingleOrDefault(candidate => candidate.Name == kind)
            ?? throw new InvalidOperationException($"There is no operation called '{kind}'.");
        var steps = services.GetServices<IOperationStep>().Where(step => step.Kind == kind).Select(step => step.Name).ToHashSet(StringComparer.Ordinal);

        if (definition.Steps.Count == 0 || definition.Steps.Any(name => !steps.Contains(name)))
        {
            throw new InvalidOperationException($"The operation '{kind}' names a step this build does not have.");
        }

        var storeId = storeContext.StoreId ?? throw new InvalidOperationException("An operation is started inside a store.");
        var tenantId = storeContext.TenantId ?? throw new InvalidOperationException("An operation is started inside a company.");

        var unfinished = await dbContext.Set<ProvisioningOperation>()
            .IgnoreQueryFilters()
            .Where(operation => operation.FinishedAt == null && (operation.StoreId == storeId || (subject != null && operation.Subject == subject)))
            .Select(operation => (Guid?)operation.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (unfinished is { } running)
        {
            return new OperationStart(running, Started: false);
        }

        var operation = new ProvisioningOperation(
            tenantId,
            storeId,
            kind,
            subject,
            input is null ? null : JsonSerializer.Serialize(input, Json),
            definition.Steps,
            clock.GetUtcNow());

        dbContext.Add(operation);
        audit.Record("operation.started", Describe(operation), new { operation = operation.Id });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new OperationStart(operation.Id, Started: true);
    }

    public static string Describe(ProvisioningOperation operation) =>
        operation.Subject is null ? operation.Kind : $"{operation.Kind} {operation.Subject}";
}
