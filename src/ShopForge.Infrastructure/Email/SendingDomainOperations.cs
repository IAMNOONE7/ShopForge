using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Provisioning;
using ShopForge.Shared.Security;

namespace ShopForge.Infrastructure.Email;

// A store's own sending domain, connected and removed as operations on the provisioning engine (D-210, D-211).
//
// Connecting: the row the request made; the domain at Mailgun, found or made, and the records it asks for; a key that
// sends for that domain alone, its secret kept first; Mailgun finding the records, which can take hours or days and is
// waited for; and the switch, after which the store's mail goes out through its own domain. Cancelled before the end,
// it takes away only what it made.
//
// Removing: the switch back to the platform's domain first, so no message is caught half way, then the key, the
// domain at Mailgun and the row.
internal static class SendingDomainOperations
{
    public const string Connect = "email.domain";
    public const string Remove = "email.domain.remove";

    // Long enough for a merchant to reach whoever runs their DNS; after that the operation stops for a person.
    public static readonly TimeSpan GivesUpWaitingAfter = TimeSpan.FromDays(7);

    public static Task<SendingDomain?> OfTheStoreAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.Set<SendingDomain>().SingleOrDefaultAsync(cancellationToken);
}

internal sealed class ConnectSendingDomain : IOperationKind
{
    public string Name => SendingDomainOperations.Connect;

    public IReadOnlyList<string> Steps => ["register", "domain", "key", "verification", "activate"];
}

internal sealed class RemoveSendingDomain : IOperationKind
{
    public string Name => SendingDomainOperations.Remove;

    public IReadOnlyList<string> Steps => ["deactivate", "key", "domain", "forget"];
}

// The row the request made in the same save as the operation, claimed here. Taken away with it on cancelling.
internal sealed class RegisterSendingDomain(DbContext dbContext, TimeProvider clock) : IOperationStep
{
    public string Kind => SendingDomainOperations.Connect;

    public string Name => "register";

    public bool CanUndo => true;

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is not { } sending
            || sending.State != SendingDomainState.Connecting
            || (sending.OperationId is { } claimed && claimed != context.OperationId))
        {
            return StepOutcome.Failed("The store has no sending domain waiting for this operation.");
        }

        sending.Begin(context.OperationId, clock.GetUtcNow());

        return StepOutcome.Done(sending.Id.ToString(), created: true);
    }

    public async Task UndoAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is { } sending && sending.OperationId == context.OperationId)
        {
            dbContext.Remove(sending);
        }
    }
}

// Asks Mailgun whether it has the domain before making it. A domain found was not made here, so cancelling leaves it.
internal sealed class MailgunSendingDomain(DbContext dbContext, MailgunApi mailgun, TimeProvider clock) : IOperationStep
{
    public string Kind => SendingDomainOperations.Connect;

    public string Name => "domain";

    public bool CanUndo => true;

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is not { } sending)
        {
            return StepOutcome.Failed("The store's sending domain is gone.");
        }

        try
        {
            var found = await mailgun.FindDomainAsync(sending.Domain, cancellationToken);
            var domain = found ?? await mailgun.CreateDomainAsync(sending.Domain, cancellationToken);
            sending.Record(domain.Records, clock.GetUtcNow());

            return StepOutcome.Done(domain.Name, created: found is null);
        }
        catch (MailgunException refused) when (refused.IsPermanent)
        {
            return StepOutcome.Failed(refused.Message);
        }
    }

    public Task UndoAsync(IStepContext context, CancellationToken cancellationToken) =>
        mailgun.DeleteDomainAsync(context.ResourceId!, cancellationToken);
}

// A key that sends for this domain alone, its secret kept before anything else. One with this step's name whose secret
// was never kept is an orphan: revoked, and another made, never recorded as configured (D-210).
internal sealed class MailgunSendingKey(DbContext dbContext, MailgunApi mailgun, TimeProvider clock) : IOperationStep
{
    public string Kind => SendingDomainOperations.Connect;

    public string Name => "key";

    public bool CanUndo => true;

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is not { } sending)
        {
            return StepOutcome.Failed("The store's sending domain is gone.");
        }

        try
        {
            string? kept = null;

            foreach (var key in await mailgun.SendingKeysAsync(sending.Domain, cancellationToken))
            {
                if (!key.Description.StartsWith(context.StableName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (kept is null && context.HoldsSecretFor(key.Id))
                {
                    kept = key.Id;
                }
                else
                {
                    await mailgun.DeleteKeyAsync(key.Id, cancellationToken);
                }
            }

            if (kept is null)
            {
                var made = await mailgun.CreateSendingKeyAsync(sending.Domain, $"{context.StableName} {context.OperationId:N}", cancellationToken);
                await context.KeepSecretAsync(made.Id, made.Secret, cancellationToken);
                kept = made.Id;
            }

            sending.UseKey(kept, context.SecretName, clock.GetUtcNow());

            return StepOutcome.Done(kept, created: true);
        }
        catch (MailgunException refused) when (refused.IsPermanent)
        {
            return StepOutcome.Failed(refused.Message);
        }
    }

    public async Task UndoAsync(IStepContext context, CancellationToken cancellationToken)
    {
        await mailgun.DeleteKeyAsync(context.ResourceId!, cancellationToken);

        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is { } sending)
        {
            sending.ForgetKey(clock.GetUtcNow());
        }
    }
}

// Mailgun looks at the domain's DNS when asked; until it finds the records, this waits, using up no tries (D-210).
internal sealed class MailgunVerification(DbContext dbContext, MailgunApi mailgun, TimeProvider clock) : IOperationStep
{
    public string Kind => SendingDomainOperations.Connect;

    public string Name => "verification";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is not { } sending)
        {
            return StepOutcome.Failed("The store's sending domain is gone.");
        }

        try
        {
            var domain = await mailgun.VerifyDomainAsync(sending.Domain, cancellationToken);
            var now = clock.GetUtcNow();
            sending.Record(domain.Records, now);

            if (domain.IsActive)
            {
                return StepOutcome.Done();
            }

            return context.WaitingSince is { } since && now - since > SendingDomainOperations.GivesUpWaitingAfter
                ? StepOutcome.Failed("Mailgun has not found the domain's DNS records in seven days. Publish them, then retry.")
                : StepOutcome.Waiting("Publish the domain's DNS records; Mailgun has not found them all yet.");
        }
        catch (MailgunException refused) when (refused.IsPermanent)
        {
            return StepOutcome.Failed(refused.Message);
        }
    }
}

internal sealed class ActivateSendingDomain(DbContext dbContext, TimeProvider clock) : IOperationStep
{
    public string Kind => SendingDomainOperations.Connect;

    public string Name => "activate";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is not { } sending)
        {
            return StepOutcome.Failed("The store's sending domain is gone.");
        }

        sending.Activate(clock.GetUtcNow());

        return StepOutcome.Done();
    }
}

internal sealed class DeactivateSendingDomain(DbContext dbContext, TimeProvider clock) : IOperationStep
{
    public string Kind => SendingDomainOperations.Remove;

    public string Name => "deactivate";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is { } sending)
        {
            sending.Deactivate(context.OperationId, clock.GetUtcNow());
        }

        return StepOutcome.Done();
    }
}

internal sealed class RevokeSendingKey(DbContext dbContext, MailgunApi mailgun, ISecretStore secrets, TimeProvider clock) : IOperationStep
{
    public string Kind => SendingDomainOperations.Remove;

    public string Name => "key";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is not { } sending)
        {
            return StepOutcome.Done();
        }

        try
        {
            if (sending.KeyId is { } keyId)
            {
                await mailgun.DeleteKeyAsync(keyId, cancellationToken);
            }

            if (sending.KeySecretName is { } secretName)
            {
                await secrets.ForgetAsync(secretName, cancellationToken);
            }

            sending.ForgetKey(clock.GetUtcNow());

            return StepOutcome.Done();
        }
        catch (MailgunException refused) when (refused.IsPermanent)
        {
            return StepOutcome.Failed(refused.Message);
        }
    }
}

internal sealed class DeleteMailgunDomain(DbContext dbContext, MailgunApi mailgun) : IOperationStep
{
    public string Kind => SendingDomainOperations.Remove;

    public string Name => "domain";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is not { } sending)
        {
            return StepOutcome.Done();
        }

        try
        {
            await mailgun.DeleteDomainAsync(sending.Domain, cancellationToken);

            return StepOutcome.Done(sending.Domain);
        }
        catch (MailgunException refused) when (refused.IsPermanent)
        {
            return StepOutcome.Failed(refused.Message);
        }
    }
}

internal sealed class ForgetSendingDomain(DbContext dbContext) : IOperationStep
{
    public string Kind => SendingDomainOperations.Remove;

    public string Name => "forget";

    public async Task<StepOutcome> RunAsync(IStepContext context, CancellationToken cancellationToken)
    {
        if (await SendingDomainOperations.OfTheStoreAsync(dbContext, cancellationToken) is { } sending)
        {
            dbContext.Remove(sending);
        }

        return StepOutcome.Done();
    }
}
