using System.Reflection;

namespace Signet.EpubCheck.Core;

/// <summary>
/// The version of the EPUBCheck port: the version of the upstream release it ports plus the build timestamp, the same
/// scheme as Signet's own version (<c>5.4.0-202611051200</c>).
/// </summary>
public static class EpubCheckVersion
{
    /// <summary>The upstream EPUBCheck release this port follows (<c>version/EpubCheckVersion.props</c>).</summary>
    public const string UpstreamRelease = "5.4.0";

    /// <summary>The full version: <see cref="UpstreamRelease"/>, <c>-</c> and the UTC build time as <c>yyyyMMddHHmm</c>.</summary>
    public static string Text { get; } = ReadText();

    private static string ReadText()
    {
        string? informational = typeof(EpubCheckVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informational) ? UpstreamRelease : informational;
    }
}
