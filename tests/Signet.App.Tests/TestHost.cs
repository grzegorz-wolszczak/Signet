using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Input;
using Signet.App.Services;
using Signet.App.Toolbars;
using Signet.Core.Misc;

namespace Signet.App.Tests;

/// <summary>
/// A set of App layer services on a throwaway settings file, for tests without DI and without Avalonia.
/// </summary>
internal sealed class TestHost : IDisposable
{
    public TestHost()
    {
        SettingsPath = Path.Combine(Path.GetTempPath(), "Signet.Tests", $"app-{Guid.NewGuid():N}.json");
        Settings = new SettingsStore(SettingsPath);
        StatusBar = new StatusBarService();
        Shortcuts = new KeyboardShortcutManager(Settings);
        Toolbars = new ToolbarManager(Settings);
        Registry = new AppActionRegistry(Shortcuts, StatusBar, NullLogger<AppActionRegistry>.Instance);
    }

    public string SettingsPath { get; }

    public SettingsStore Settings { get; }

    public StatusBarService StatusBar { get; }

    public KeyboardShortcutManager Shortcuts { get; }

    public ToolbarManager Toolbars { get; }

    public AppActionRegistry Registry { get; }

    /// <summary>Creates a new settings store on the same file (simulates a restart).</summary>
    public SettingsStore ReopenSettings() => new(SettingsPath);

    public void Dispose()
    {
        StatusBar.Dispose();
        try
        {
            if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }
        }
        catch (IOException)
        {
            // best-effort
        }
    }
}
