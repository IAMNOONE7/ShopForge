namespace ShopForge.Infrastructure.Persistence;

public sealed class TenancyViolationException(string message) : InvalidOperationException(message);
