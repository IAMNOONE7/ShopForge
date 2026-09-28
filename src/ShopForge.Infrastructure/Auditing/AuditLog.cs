using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Infrastructure.Auditing;

internal sealed class AuditLog(
    DbContext dbContext,
    IStoreContext storeContext,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider clock) : IAuditLog
{
    public void Record(string action, string subject, object? details = null) =>
        Add(storeContext.TenantId, storeContext.StoreId, action, subject, details);

    public void RecordForTenant(Guid tenantId, string action, string subject, object? details = null) =>
        Add(tenantId, storeId: null, action, subject, details);

    private void Add(Guid? tenantId, Guid? storeId, string action, string subject, object? details)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var identity = httpContext?.User.Identities.FirstOrDefault(candidate => candidate.IsAuthenticated);

        dbContext.Add(new AuditEntry(
            tenantId,
            storeId,
            KindOf(identity),
            Guid.TryParse(identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var actorId) ? actorId : null,
            identity?.FindFirst(ClaimTypes.Email)?.Value,
            action,
            subject,
            details is null ? null : JsonSerializer.Serialize(details, AuditJson.Options),
            httpContext?.Connection.RemoteIpAddress?.ToString(),
            clock.GetUtcNow()));
    }

    // Each of the three kinds of session signs in under its own scheme (D-103), which is what the identity carries;
    // work with no session behind it — a background sweep, the outbox — is the platform acting on its own.
    private static AuditActorKind KindOf(ClaimsIdentity? identity) => identity?.AuthenticationType switch
    {
        CookieAuthenticationDefaults.AuthenticationScheme => AuditActorKind.TenantUser,
        PlatformPolicies.Scheme => AuditActorKind.PlatformUser,
        CustomerPolicies.Scheme => AuditActorKind.Customer,
        _ => AuditActorKind.System,
    };
}

internal static class AuditJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
