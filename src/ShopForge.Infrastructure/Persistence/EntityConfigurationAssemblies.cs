using System.Reflection;

namespace ShopForge.Infrastructure.Persistence;

public sealed record EntityConfigurationAssemblies(IReadOnlyCollection<Assembly> Assemblies);
