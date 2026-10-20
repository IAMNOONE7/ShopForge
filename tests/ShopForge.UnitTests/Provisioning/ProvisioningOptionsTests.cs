using ShopForge.Infrastructure.Provisioning;
using ShopForge.Shared.Provisioning;

namespace ShopForge.UnitTests.Provisioning;

// How long the engine leaves a step alone: a failure backs off to half an hour and is given up on at the eighth try,
// and a wait backs off to an hour, or to what the step asked for within six (D-210).
public sealed class ProvisioningOptionsTests
{
    private static readonly ProvisioningOptions Options = ProvisioningOptions.Default;
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_failure_waits_thirty_seconds_doubling_to_half_an_hour()
    {
        var waits = Enumerable.Range(1, 8).Select(Options.RetryAfter).Select(wait => wait.TotalSeconds);

        Assert.Equal([30, 60, 120, 240, 480, 960, 1800, 1800], waits);
    }

    [Fact]
    public void A_wait_is_a_minute_doubling_to_an_hour_unless_the_step_asks_for_its_own()
    {
        var waits = Enumerable.Range(1, 8).Select(checks => Options.CheckAfter(checks, asked: null).TotalMinutes);

        Assert.Equal([1, 2, 4, 8, 16, 32, 60, 60], waits);
        Assert.Equal(TimeSpan.FromMinutes(10), Options.CheckAfter(1, TimeSpan.FromMinutes(10)));
        Assert.Equal(TimeSpan.FromHours(6), Options.CheckAfter(1, TimeSpan.FromHours(12)));
        Assert.Equal(TimeSpan.FromMinutes(1), Options.CheckAfter(1, TimeSpan.Zero));
    }

    [Fact]
    public void The_eighth_failure_stops_the_operation_for_a_person()
    {
        var operation = new ProvisioningOperation(Guid.NewGuid(), Guid.NewGuid(), "test", null, null, ["only"], Now);
        var step = operation.Steps[0];

        for (var attempt = 1; attempt < Options.MaxAttempts; attempt++)
        {
            operation.Record(step, StepOutcome.Retry("down"), null, Now, Options);
        }

        var stillTrying = (operation.State, step.Status, operation.NextCheckAt);
        var last = operation.Record(step, StepOutcome.Retry("down"), null, Now, Options);

        Assert.Equal((OperationState.Running, StepStatus.Pending, Now + Options.RetryAfter(7)), stillTrying);
        Assert.Equal(["operation.step-failed"], last);
        Assert.Equal((OperationState.Failed, StepStatus.Failed, 8), (operation.State, step.Status, step.Attempts));
        Assert.Null(operation.NextCheckAt);
    }

    // What a person may do follows from where the operation stopped, and nothing may be done to one that is over.
    [Fact]
    public void Each_state_offers_only_the_actions_that_apply_to_it()
    {
        var waiting = Operation();
        waiting.Record(waiting.Steps[0], StepOutcome.NeedsInput("Say so."), null, Now, Options);
        var failed = Operation();
        failed.Record(failed.Steps[0], StepOutcome.Failed("No."), null, Now, Options);
        var cancelling = Operation();
        cancelling.RequestCancel(Now);
        var succeeded = Operation();
        succeeded.Record(succeeded.Steps[0], StepOutcome.Done(), null, Now, Options);

        Assert.Equal((false, false, true), Actions(Operation()));
        Assert.Equal((false, true, true), Actions(waiting));
        Assert.Equal((true, false, true), Actions(failed));
        Assert.Equal((false, false, false), Actions(cancelling));
        Assert.Equal((false, false, false), Actions(succeeded));
        Assert.Equal(OperationState.Succeeded, succeeded.State);
        Assert.NotNull(succeeded.FinishedAt);
    }

    // Newest first, and only what the operation made: a step that found its resource is not undone.
    [Fact]
    public void A_cancellation_undoes_what_was_made_newest_first()
    {
        var operation = new ProvisioningOperation(Guid.NewGuid(), Guid.NewGuid(), "test", null, null, ["first", "found", "last", "waiting"], Now);
        operation.Record(operation.Steps[0], StepOutcome.Done("one", created: true), null, Now, Options);
        operation.Record(operation.Steps[1], StepOutcome.Done("two", created: false), null, Now, Options);
        operation.Record(operation.Steps[2], StepOutcome.Done("three", created: true), null, Now, Options);
        operation.Record(operation.Steps[3], StepOutcome.Waiting("Soon."), null, Now, Options);

        operation.RequestCancel(Now);
        var undoneFirst = operation.NextToUndo?.Name;
        operation.RecordUndone(operation.NextToUndo!, removed: true, Now);
        var undoneSecond = operation.NextToUndo?.Name;
        operation.RecordUndone(operation.NextToUndo!, removed: false, Now);

        Assert.Equal(("last", "first"), (undoneFirst, undoneSecond));
        Assert.Null(operation.NextToUndo);
        Assert.Equal(StepStatus.Pending, operation.Steps[3].Status);
        Assert.Equal(["operation.cancelled"], operation.CompleteCancel(Now));
        Assert.Equal((OperationState.Cancelled, StepStatus.Undone, StepStatus.Kept), (operation.State, operation.Steps[2].Status, operation.Steps[0].Status));
    }

    // What a retry asks the provider for is what the first run made: the same store, kind, step and subject give the
    // same name on every run and every replica, and another subject a name of its own.
    [Fact]
    public void A_step_s_stable_name_is_the_same_on_every_run_and_differs_by_subject()
    {
        var store = Guid.NewGuid();
        var first = NameOf(store, "shop.example.test");
        var again = NameOf(store, "shop.example.test");
        var other = NameOf(store, "other.example.test");
        var none = NameOf(store, null);

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.Equal($"shopforge-{store:N}-domain-connect-zone", none);
        Assert.StartsWith(none + "-", first, StringComparison.Ordinal);
    }

    private static string NameOf(Guid store, string? subject)
    {
        var operation = new ProvisioningOperation(Guid.NewGuid(), store, "domain.connect", subject, null, ["zone"], Now);

        return new StepRun(operation, operation.Steps[0], null!, null!, TimeProvider.System).StableName;
    }

    private static ProvisioningOperation Operation() => new(Guid.NewGuid(), Guid.NewGuid(), "test", null, null, ["only"], Now);

    private static (bool Retry, bool Continue, bool Cancel) Actions(ProvisioningOperation operation) =>
        (operation.CanRetry, operation.CanContinue, operation.CanCancel);
}
