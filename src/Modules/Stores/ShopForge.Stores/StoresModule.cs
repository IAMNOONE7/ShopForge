using System.Reflection;

namespace ShopForge.Stores;

public static class StoresModule
{
    internal const string Schema = "stores";

    public static Assembly Assembly => typeof(StoresModule).Assembly;
}
