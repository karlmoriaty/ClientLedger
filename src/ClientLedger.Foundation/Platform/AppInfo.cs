using System.Reflection;

namespace ClientLedger.Foundation.Platform;

/// <summary>版本与产品标识的唯一来源，"关于"页与审计都用它。</summary>
public sealed class AppInfo
{
    public AppInfo(Assembly assembly)
    {
        var name = assembly.GetName();
        Version = name.Version ?? new Version(0, 1, 0, 0);

        ProductName = assembly
            .GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? "ClientLedger";

        Company = assembly
            .GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
            ?? "karlmoriaty";

        Copyright = assembly
            .GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
            ?? string.Empty;
    }

    public static AppInfo Current { get; } = new(typeof(AppInfo).Assembly);

    public string ProductName { get; }

    public Version Version { get; }

    /// <summary>三段式版本，用于展示与更新通道比较。</summary>
    public string DisplayVersion => Version.ToString(3);

    public string Company { get; }

    public string Copyright { get; }
}