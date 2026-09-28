namespace ShopForge.Shared.Dns;

// Asking the world what a name says about itself. Behind an interface because a domain check that really talks to
// DNS is not something a test can arrange, and because the answer is the only evidence a merchant owns a domain
// (D-124).
public interface IDnsTxtRecords
{
    Task<IReadOnlyList<string>> LookupAsync(string name, CancellationToken cancellationToken);
}
