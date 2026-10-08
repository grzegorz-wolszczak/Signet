using System;
using System.Reflection;

namespace Signet.Core;

/// <summary>
/// Constants identifying the application. Used among others for the OPF "generator"
/// <c>&lt;meta&gt;</c>, the window title and the "About" dialog.
/// </summary>
public static class ApplicationInfo
{
    /// <summary>Product name shown to the user.</summary>
    public const string Name = "Signet";

    /// <summary>
    /// Application version read from <see cref="AssemblyInformationalVersionAttribute"/>
    /// (or, if missing, from the assembly version). Never returns <see langword="null"/>.
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>
    /// "Name + version" label, e.g. <c>Signet 0.5.0</c> - the form used in the generator
    /// metadata and in the About dialog.
    /// </summary>
    public static string NameWithVersion => $"{Name} {Version}";

    private static string ResolveVersion()
    {
        Assembly assembly = typeof(ApplicationInfo).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the build metadata suffix (e.g. "0.5.0+abc123") - irrelevant to the user.
            int plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        Version? assemblyVersion = assembly.GetName().Version;
        return assemblyVersion?.ToString() ?? "0.0.0";
    }
}
