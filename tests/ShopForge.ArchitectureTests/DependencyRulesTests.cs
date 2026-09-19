using System.Reflection;
using ShopForge.Access;
using ShopForge.Infrastructure.Persistence;
using ShopForge.Shared.Tenancy;
using ShopForge.Stores;

namespace ShopForge.ArchitectureTests;

public sealed class DependencyRulesTests
{
    private static readonly Assembly Shared = typeof(IStoreContext).Assembly;
    private static readonly Assembly Infrastructure = typeof(ShopForgeDbContext).Assembly;
    private static readonly Assembly[] Modules = [StoresModule.Assembly, AccessModule.Assembly];

    [Fact]
    public void Shared_does_not_depend_on_other_ShopForge_projects()
    {
        Assert.Empty(ShopForgeReferencesOf(Shared));
    }

    [Fact]
    public void Infrastructure_depends_on_Shared_only()
    {
        Assert.Equal(["ShopForge.Shared"], ShopForgeReferencesOf(Infrastructure));
    }

    [Fact]
    public void Modules_depend_on_Shared_only()
    {
        Assert.All(Modules, module => Assert.Equal(["ShopForge.Shared"], ShopForgeReferencesOf(module)));
    }

    [Fact]
    public void Module_domain_types_are_not_public()
    {
        var publicDomainTypes = Modules
            .SelectMany(module => module.GetTypes())
            .Where(type => type.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true && type.IsPublic)
            .Select(type => type.FullName);

        Assert.Empty(publicDomainTypes);
    }

    private static string[] ShopForgeReferencesOf(Assembly assembly) =>
    [
        .. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("ShopForge.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal),
    ];
}
