using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Signet.App.Infrastructure;

/// <summary>
/// What the process was started with, for the log at startup: the runtime, the command-line arguments and the environment
/// variables that change how the runtime, the UI or the preview engine behave (e.g. <c>DOTNET_EnableCrashReport</c> for a
/// crash report, <c>WEBKIT_*</c>). Only those: the rest of the environment may hold anything, secrets included.
/// </summary>
public static class StartupEnvironment
{
    /// <summary>Prefixes of the variables logged (case-insensitive).</summary>
    public static IReadOnlyList<string> VariablePrefixes { get; } =
    [
        "DOTNET_", "COREHOST_", "COMPlus_", "SIGNET_", "AVALONIA_", "WEBKIT_", "GTK_", "GDK_", "GSK_", "G_", "LIBGL_",
    ];

    /// <summary>Single variables logged.</summary>
    public static IReadOnlyList<string> VariableNames { get; } =
    [
        "DISPLAY", "WAYLAND_DISPLAY", "XDG_SESSION_TYPE", "XDG_CURRENT_DESKTOP", "LANG", "LC_ALL",
    ];

    /// <summary>The description of the current process.</summary>
    public static string Describe(IReadOnlyList<string> args) =>
        Describe(args, Environment.GetEnvironmentVariables());

    /// <summary>
    /// The description: the runtime and the OS, the arguments (or "none") and the selected variables sorted by name (or
    /// "none"), one per line.
    /// </summary>
    public static string Describe(IReadOnlyList<string> args, IDictionary environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        List<string> variables = environment.Cast<DictionaryEntry>()
            .Select(e => (Name: e.Key.ToString() ?? string.Empty, Value: e.Value?.ToString() ?? string.Empty))
            .Where(e => IsLogged(e.Name))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => $"{e.Name}={e.Value}")
            .ToList();

        string nl = Environment.NewLine;
        return $"Runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})"
            + $"{nl}  Process: {Environment.ProcessPath}"
            + $"{nl}  Arguments: {(args.Count == 0 ? "none" : string.Join(" ", args.Select(Quote)))}"
            + $"{nl}  Environment: {(variables.Count == 0 ? "none" : nl + "    " + string.Join(nl + "    ", variables))}";
    }

    private static bool IsLogged(string name) =>
        VariableNames.Contains(name, StringComparer.Ordinal)
        || VariablePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static string Quote(string arg) => arg.Length == 0 || arg.Any(char.IsWhiteSpace) ? $"\"{arg}\"" : arg;
}
