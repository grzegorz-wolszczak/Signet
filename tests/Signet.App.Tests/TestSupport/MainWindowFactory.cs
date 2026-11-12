using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Input;
using Signet.App.Services;
using Signet.App.Tabs;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.Tests.TestSupport;

/// <summary>The main window over given settings, for the tests that need two windows on the same settings (one after the other).</summary>
internal static class MainWindowFactory
{
    /// <summary>
    /// The main window on the given settings (the parameterless constructor uses a settings file of its own); the
    /// dictionaries and the custom preview styles are looked up in the folder of the settings file. The window is
    /// disposed with <paramref name="disposables"/>.
    /// </summary>
    public static MainWindowViewModel On(SettingsStore settings, Disposables disposables)
    {
        string folder = Path.GetDirectoryName(settings.FilePath)!;
        StatusBarService statusBar = new();
        KeyboardShortcutManager shortcuts = new(settings);
        MissingDoctypeGuard doctypeGuard = new(settings);
        BookBrowserViewModel bookBrowser = new(settings, statusBar, doctypeGuard);
        PreviewViewModel preview = new(statusBar, folder);
        MainDockFactory dockFactory = new(bookBrowser, preview);
        SpellChecker spellChecker = new(settings, Path.Combine(folder, "hunspell_dictionaries"), Path.Combine(folder, "user_dictionaries"));
        MainWindowViewModel vm = disposables.Add(new MainWindowViewModel(
            NullLogger<MainWindowViewModel>.Instance,
            new AppActionRegistry(shortcuts, statusBar, NullLogger<AppActionRegistry>.Instance), shortcuts,
            new ToolbarManager(settings), dockFactory, statusBar, settings, bookBrowser,
            new TabManager(dockFactory, statusBar, settings, spellChecker), preview, spellChecker,
            new ClipboardHistoryService(settings), doctypeGuard));
        vm.AttachFileWorkflowPrompts(new FakeFileWorkflowPrompts());
        return vm;
    }
}
