using ShopForge.Shared.Edge;

namespace ShopForge.Infrastructure.Edge;

// A deployment that binds no domain at the edge: development, and every test that is not about the edge. Nothing starts
// an operation against it, so a step that reaches it was started by mistake and is told so.
internal sealed class NoEdgeDomains : IEdgeDomains
{
    public bool IsConfigured => false;

    public string TrafficTarget => string.Empty;

    public Task<EdgeDomain?> FindAsync(string hostName, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<EdgeDomain> CreateAsync(string hostName, CancellationToken cancellationToken) => throw NotConfigured();

    public Task RefreshValidationAsync(EdgeDomain domain, CancellationToken cancellationToken) => throw NotConfigured();

    public Task DeleteAsync(string hostName, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<bool> JoinAsync(string hostName, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<bool> LeaveAsync(string hostName, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<EdgeMembership> MembershipAsync(string hostName, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<IReadOnlyList<EdgeDomain>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<EdgeDomain>>([]);

    private static InvalidOperationException NotConfigured() => new("This deployment binds no domain at the edge: Edge:Domains is not configured.");
}
