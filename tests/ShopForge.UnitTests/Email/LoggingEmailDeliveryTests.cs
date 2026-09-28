using Microsoft.Extensions.Logging;
using ShopForge.Infrastructure.Email;
using ShopForge.Shared.Email;

namespace ShopForge.UnitTests.Email;

public sealed class LoggingEmailDeliveryTests
{
    // Until there is a provider, the log is where mail goes — which is exactly why a reset link must not be in
    // the line an ordinary deployment keeps (D-119).
    [Fact]
    public async Task The_ordinary_line_names_the_recipient_and_not_what_was_written_to_them()
    {
        var recorded = new RecordingLogger();
        var delivery = new LoggingEmailDelivery(recorded);

        await delivery.DeliverAsync(
            new EmailMessage("someone@example.test", "Reset your password", "Use this link: https://shop.test/reset?token=SECRET-VALUE"),
            TestContext.Current.CancellationToken);

        var information = recorded.Lines.Where(line => line.Level == LogLevel.Information).Select(line => line.Message).ToList();
        var debug = recorded.Lines.Where(line => line.Level == LogLevel.Debug).Select(line => line.Message).ToList();

        Assert.Contains(information, line => line.Contains("someone@example.test", StringComparison.Ordinal));
        Assert.DoesNotContain(information, line => line.Contains("SECRET-VALUE", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("SECRET-VALUE", StringComparison.Ordinal));
    }

    private sealed class RecordingLogger : ILogger<LoggingEmailDelivery>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }
}
