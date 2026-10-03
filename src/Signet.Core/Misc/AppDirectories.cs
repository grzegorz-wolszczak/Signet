using System;
using System.IO;

namespace Signet.Core.Misc;

/// <summary>
/// Determines the application data folders (settings, logs, auxiliary files).
/// Honors the <c>SIGNET_PREFS_DIR</c> environment variable, otherwise uses the user's
/// local data folder (<see cref="Environment.SpecialFolder.LocalApplicationData"/>) +
/// <c>Signet</c>.
/// </summary>
public static class AppDirectories
{
    /// <summary>The name of the environment variable overriding the preferences folder.</summary>
    public const string PrefsDirEnvVar = "SIGNET_PREFS_DIR";

    /// <summary>The settings file name in the preferences folder.</summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>
    /// The user's preferences folder. Created on first read.
    /// </summary>
    public static string PrefsDirectory { get; } = ResolvePrefsDirectory();

    /// <summary>The full path of the settings file (<c>settings.json</c>).</summary>
    public static string SettingsFilePath => Path.Combine(PrefsDirectory, SettingsFileName);

    /// <summary>The folder for log files (<c>&lt;prefs&gt;/logs</c>).</summary>
    public static string LogsDirectory => Path.Combine(PrefsDirectory, "logs");

    /// <summary>
    /// The folder for additional spelling dictionaries installed by the user (<c>.aff</c>/<c>.dic</c>
    /// pairs) — besides the built-in <c>en_US</c>.
    /// </summary>
    public static string HunspellDictionariesDirectory => Path.Combine(PrefsDirectory, "hunspell_dictionaries");

    /// <summary>
    /// The folder for the user's personal dictionaries (lists of added words, one file = one
    /// dictionary).
    /// </summary>
    public static string UserDictionariesDirectory => Path.Combine(PrefsDirectory, "user_dictionaries");

    /// <summary>Ensures the preferences folder exists and returns its path.</summary>
    public static string EnsurePrefsDirectory()
    {
        Directory.CreateDirectory(PrefsDirectory);
        return PrefsDirectory;
    }

    /// <summary>Ensures the logs folder exists and returns its path.</summary>
    public static string EnsureLogsDirectory()
    {
        Directory.CreateDirectory(LogsDirectory);
        return LogsDirectory;
    }

    private static string ResolvePrefsDirectory()
    {
        string? overridePath = Environment.GetEnvironmentVariable(PrefsDirEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath.Trim());
        }

        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrEmpty(root))
        {
            // As a fallback (e.g. some Linux containers without XDG/HOME) — the current directory.
            root = Directory.GetCurrentDirectory();
        }

        return Path.Combine(root, ApplicationInfo.Name);
    }
}
