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

    /// <summary>The date of the build as <c>yyyy-MM-dd</c> (from the timestamp in <see cref="Text"/>), or <c>unknown</c>.</summary>
    public static string BuildDate { get; } = ReadBuildDate();

    private static string ReadBuildDate()
    {
        int dash = Text.LastIndexOf('-');
        string stamp = dash >= 0 ? Text[(dash + 1)..] : string.Empty;
        return stamp.Length == 12 && System.DateTime.TryParseExact(stamp, "yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out System.DateTime built)
            ? built.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : "unknown";
    }

    private static string ReadText()
    {
        string? informational = typeof(EpubCheckVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informational) ? UpstreamRelease : informational;
    }
}
