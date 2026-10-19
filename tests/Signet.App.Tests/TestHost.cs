using System;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Input;
using Signet.App.Services;
using Signet.App.Toolbars;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.Tests;

/// <summary>
/// A set of App layer services on a throwaway settings file, for tests without DI and without Avalonia.
/// </summary>
internal sealed class TestHost : IDisposable
{
    // The folder of the settings file and of what is saved beside it; deleted with the host.
    private readonly TempDir _folder = new("app-");

    public TestHost()
    {
        SettingsPath = _folder.Combine("settings.xml");
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
        _folder.Dispose();
    }
}
