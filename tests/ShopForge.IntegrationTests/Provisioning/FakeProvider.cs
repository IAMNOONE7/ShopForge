using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Shared.Provisioning;

namespace ShopForge.IntegrationTests.Provisioning;

// The provider behind the test operation. It keeps what it was asked to make, by name and tagged with the operation
// that made it, and on each call does what the test told it to: answer, fail, refuse, hang, do the work and lose the
// answer, or stop the worker dead before or after the work, as a crash would (D-210).
internal sealed class FakeProvider
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<Behaviour>> _next = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, Made> Purchases { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, Made> Resources { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, FakeCredential> Credentials { get; } = new(StringComparer.Ordinal);

    // How many times each call did its work, which is what "one effect" counts.
    public ConcurrentDictionary<string, int> Effects { get; } = new(StringComparer.Ordinal);

    public bool Ready { get; set; } = true;

    public bool Approved { get; set; } = true;

    // The worker the test is running, which a crash stops.
    public CancellationTokenSource? Worker { get; set; }

    // What the test does in the middle of a step it was told to interleave with.
    public Func<Task>? During { get; set; }

    public int EffectsOf(string point) => Effects.GetValueOrDefault(point);

    public void Next(string point, params Behaviour[] behaviours)
    {
        lock (_gate)
        {
            if (!_next.TryGetValue(point, out var queue))
            {
                _next[point] = queue = new Queue<Behaviour>();
            }

            foreach (var behaviour in behaviours)
            {
                queue.Enqueue(behaviour);
            }
        }
    }

    public async Task<T> CallAsync<T>(string point, Func<T> work, CancellationToken cancellationToken)
    {
        var behaviour = Take(point);

        switch (behaviour)
        {
            case Behaviour.Fail:
                throw new HttpRequestException($"{point}: the provider is unavailable.");
            case Behaviour.Refuse:
                throw new ProviderRefusedException($"{point}: the provider refused.");
            // Longer than any step's timeout, and finite, so an engine that stopped timing steps out fails a test
            // rather than hanging the suite.
            case Behaviour.Hang:
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                break;
            case Behaviour.CrashBefore:
                Crash(cancellationToken);
                break;
            case Behaviour.Interleave:
                await (During ?? throw new InvalidOperationException("Nothing to interleave."))();
                break;
        }

        var result = work();
        Effects.AddOrUpdate(point, 1, (_, count) => count + 1);

        switch (behaviour)
        {
            case Behaviour.AnswerLost:
                throw new TimeoutException($"{point}: the answer was lost on the way back.");
            case Behaviour.CrashAfter:
                Crash(cancellationToken);
                break;
        }

        return result;
    }

    public FakeCredential CreateCredential(string name, Guid operationId)
    {
        var credential = new FakeCredential($"credential-{Guid.NewGuid():N}", name, operationId, $"secret-{Guid.NewGuid():N}");
        Credentials[credential.Id] = credential;

        return credential;
    }

    private Behaviour Take(string point)
    {
        lock (_gate)
        {
            return _next.TryGetValue(point, out var queue) && queue.TryDequeue(out var behaviour) ? behaviour : Behaviour.Answer;
        }
    }

    private void Crash(CancellationToken cancellationToken)
    {
        (Worker ?? throw new InvalidOperationException("A crash needs a worker to stop.")).Cancel();
        cancellationToken.ThrowIfCancellationRequested();
    }
}

public enum Behaviour
{
    Answer,
    Fail,
    Refuse,
    Hang,
    AnswerLost,
    CrashBefore,
    CrashAfter,
    Interleave,
}

internal sealed record Made(string Id, Guid OperationId);

internal sealed class FakeCredential(string id, string name, Guid operationId, string secret)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public Guid OperationId { get; } = operationId;

    public string Secret { get; } = secret;

    public bool Revoked { get; set; }
}

internal sealed class ProviderRefusedException(string message) : Exception(message);

// The operation the engine is proven with: something bought, something made, a credential with a secret, a wait for
// the provider, and a person's say-so. Real kinds arrive with V15 and V16.
internal static class TestOperation
{
    public const string Kind = "test.launch";

    public static readonly string[] Steps = ["purchase", "resource", "credential", "ready", "approval"];

    public static IServiceCollection AddTestOperation(this IServiceCollection services)
    {
        services.AddSingleton<IOperationKind, TestKind>();
        services.AddScoped<IOperationStep, PurchaseStep>();
        services.AddScoped<IOperationStep, ResourceStep>();
        services.AddScoped<IOperationStep, CredentialStep>();
        services.AddScoped<IOperationStep, ReadyStep>();
        services.AddScoped<IOperationStep, ApprovalStep>();

        return services;
    }

    // What a step's stable name is for this store, worked out the way the engine does, so a test can put something
    // there before the operation runs.
    public static string StableName(Guid storeId, string step) => $"shopforge-{storeId:N}-test-launch-{step}";

    private sealed class TestKind : IOperationKind
    {
        public string Name => Kind;

        public IReadOnlyList<string> Steps => TestOperation.Steps;
    }
}

// Bought, so never taken back.
internal sealed class PurchaseStep(FakeProvider provider) : IOperationStep
{
    public string Kind => TestOperation.Kind;

    public string Name => "purchase";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (provider.Purchases.TryGetValue(context.StableName, out var found))
        {
            return StepOutcome.Done(found.Id, created: found.OperationId == context.OperationId);
        }

        var made = await provider.CallAsync(
            "purchase", () => provider.Purchases.GetOrAdd(context.StableName, _ => new Made($"purchase-{Guid.NewGuid():N}", context.OperationId)), cancellationToken);

        return StepOutcome.Done(made.Id, created: true, requestId: $"request-{Guid.NewGuid():N}");
    }
}

internal sealed class ResourceStep(FakeProvider provider) : IOperationStep
{
    public string Kind => TestOperation.Kind;

    public string Name => "resource";

    public bool CanUndo => true;

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (provider.Resources.TryGetValue(context.StableName, out var found))
        {
            return StepOutcome.Done(found.Id, created: found.OperationId == context.OperationId, data: new ResourceMade(found.Id));
        }

        try
        {
            var made = await provider.CallAsync(
                "resource", () => provider.Resources.GetOrAdd(context.StableName, _ => new Made($"resource-{Guid.NewGuid():N}", context.OperationId)), cancellationToken);

            return StepOutcome.Done(made.Id, created: true, requestId: $"request-{Guid.NewGuid():N}", data: new ResourceMade(made.Id));
        }
        catch (ProviderRefusedException refused)
        {
            return StepOutcome.Failed(refused.Message);
        }
    }

    public Task UndoAsync(IStepContext context, CancellationToken cancellationToken) =>
        provider.CallAsync("resource.delete", () => provider.Resources.TryRemove(context.StableName, out _), cancellationToken);
}

internal sealed record ResourceMade(string Id);

// A credential whose secret is kept before anything else, and one found without its secret is revoked and replaced.
internal sealed class CredentialStep(FakeProvider provider) : IOperationStep
{
    public string Kind => TestOperation.Kind;

    public string Name => "credential";

    public bool CanUndo => true;

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        var resource = context.Output<ResourceMade>("resource") ?? throw new InvalidOperationException("The resource step handed nothing on.");
        var live = provider.Credentials.Values.SingleOrDefault(credential => credential.Name == context.StableName && !credential.Revoked);

        if (live is not null)
        {
            if (context.HoldsSecretFor(live.Id))
            {
                return StepOutcome.Done(live.Id, created: live.OperationId == context.OperationId, data: new { resource.Id });
            }

            await provider.CallAsync("credential.revoke", () => live.Revoked = true, cancellationToken);
        }

        var made = await provider.CallAsync("credential.create", () => provider.CreateCredential(context.StableName, context.OperationId), cancellationToken);
        await context.KeepSecretAsync(made.Id, made.Secret, cancellationToken);
        await provider.CallAsync("credential.kept", () => true, cancellationToken);

        return StepOutcome.Done(made.Id, created: true, requestId: $"request-{Guid.NewGuid():N}", data: new { resource.Id });
    }

    public Task UndoAsync(IStepContext context, CancellationToken cancellationToken) =>
        provider.CallAsync("credential.revoke", () => provider.Credentials[context.ResourceId!].Revoked = true, cancellationToken);
}

internal sealed class ReadyStep(FakeProvider provider) : IOperationStep
{
    public string Kind => TestOperation.Kind;

    public string Name => "ready";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) =>
        await provider.CallAsync("ready", () => provider.Ready, cancellationToken)
            ? StepOutcome.Done()
            : StepOutcome.Waiting("The certificate is being issued.");
}

internal sealed class ApprovalStep(FakeProvider provider) : IOperationStep
{
    public string Kind => TestOperation.Kind;

    public string Name => "approval";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken) =>
        await provider.CallAsync("approval", () => provider.Approved, cancellationToken)
            ? StepOutcome.Done()
            : StepOutcome.NeedsInput("Point the domain's nameservers at the platform, then continue.");
}
