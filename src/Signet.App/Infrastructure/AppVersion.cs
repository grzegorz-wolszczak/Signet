using System.Reflection;

namespace Signet.App.Infrastructure;

/// <summary>
/// Application version in the form <c>0.6.0-&lt;yyyyMMddHHmm UTC&gt;</c>, compiled in as
/// <see cref="AssemblyInformationalVersionAttribute"/> by <c>version/Versioning.targets</c>.
/// </summary>
public static class AppVersion
{
    /// <summary>Full version string, e.g. <c>0.6.0-202610021432</c>.</summary>
    public static string Text { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.6.0";
}
