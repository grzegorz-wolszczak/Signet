using System.Reflection;

namespace Signet.App.Infrastructure;

/// <summary>
/// Application version in the form <c>&lt;VersionPrefix&gt;-&lt;yyyyMMddHHmm UTC&gt;</c>, compiled in as
/// <see cref="AssemblyInformationalVersionAttribute"/> by <c>version/Versioning.targets</c>.
/// </summary>
public static class AppVersion
{
    /// <summary>Full version string, e.g. <c>0.9.0-202611021432</c>; the assembly version's prefix when it is missing.</summary>
    public static string Text { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
