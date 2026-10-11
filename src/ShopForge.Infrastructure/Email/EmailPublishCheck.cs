using ShopForge.Shared.Stores;

namespace ShopForge.Infrastructure.Email;

// Whether mail actually leaves the building. The log provider is a successful send that nobody receives, so a
// deployment left on it would confirm orders to a log file and tell the merchant everything was fine (D-190).
//
// Advice rather than a refusal, deliberately: this is the operator's configuration and not the merchant's, and
// refusing a merchant's publication for it punishes the wrong person — besides which it would stop every
// publish in development and in the test suite, where the log provider is the right answer. The place for this
// to be fatal is the deployment itself, which should not accept a release that would send nothing.
internal sealed class EmailPublishCheck(EmailOptions options) : IStorePublishCheck
{
    public Task<IReadOnlyList<StoreReadiness>> CheckAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoreReadiness>>(
        [
            options.Provider == EmailOptions.LogProvider
                ? StoreReadiness.NotReady(
                    "platform.email",
                    "This deployment writes e-mail to its log instead of sending it, so order confirmations will not reach anybody.",
                    ReadinessWeight.Advised)
                : StoreReadiness.Ready("platform.email", ReadinessWeight.Advised),

            options.SenderAddress.Length > 0
                ? StoreReadiness.Ready("platform.email-sender", ReadinessWeight.Advised)
                : StoreReadiness.NotReady(
                    "platform.email-sender",
                    "This deployment has no sender address configured, so mail has nothing to come from.",
                    ReadinessWeight.Advised),
        ]);
}
