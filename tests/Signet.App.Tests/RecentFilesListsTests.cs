using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Input;
using Signet.App.Menu;
using Signet.App.Services;
using Signet.App.Tabs;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The one list of recent files shown on the start screen and (its first entries) in the File menu.</summary>
public sealed class RecentFilesListsTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (SettingsStore Settings, MainWindowViewModel Vm) New(int fileMenuLimit, int startScreenLimit, int files)
    {
        TempDir temp = _disposables.Add(new TempDir());
        SettingsStore settings = new(temp.Combine("settings.xml"));
        settings.RecentFilesLimit = fileMenuLimit;
        settings.StartScreenRecentFilesLimit = startScreenLimit;
        settings.RecentFiles = Enumerable.Range(0, files).Select(i => temp.Combine("book" + i + ".epub")).ToArray();

        string folder = Path.GetDirectoryName(settings.FilePath)!;
        StatusBarService statusBar = new();
        KeyboardShortcutManager shortcuts = new(settings);
        MissingDoctypeGuard doctypeGuard = new(settings);
        BookBrowserViewModel bookBrowser = new(settings, statusBar, doctypeGuard);
        PreviewViewModel preview = new(statusBar, folder);
        MainDockFactory dockFactory = new(bookBrowser, preview);
        SpellChecker spellChecker = new(settings, Path.Combine(folder, "hunspell_dictionaries"), Path.Combine(folder, "user_dictionaries"));
        MainWindowViewModel vm = _disposables.Add(new MainWindowViewModel(
            NullLogger<MainWindowViewModel>.Instance,
            new AppActionRegistry(shortcuts, statusBar, NullLogger<AppActionRegistry>.Instance), shortcuts,
            new ToolbarManager(settings), dockFactory, statusBar, settings, bookBrowser,
            new TabManager(dockFactory, statusBar, settings, spellChecker), preview, spellChecker,
            new ClipboardHistoryService(settings), doctypeGuard));
        vm.AttachFileWorkflowPrompts(new FakeFileWorkflowPrompts());
        return (settings, vm);
    }

    [Fact]
    public void The_start_screen_shows_more_recent_files_than_the_file_menu()
    {
        // Arrange
        (SettingsStore settings, MainWindowViewModel vm) = New(fileMenuLimit: 3, startScreenLimit: 10, files: 15);

        // Act
        IRecentFilesMenu menu = vm;

        // Assert
        menu.RecentFiles.Should().Equal(settings.RecentFiles.Take(3), "the menu shows the first entries of the list");
        vm.StartScreen.RecentFileItems.Select(f => f.Path).Should().Equal(settings.RecentFiles.Take(10));
    }

    [Fact]
    public void Removing_an_entry_on_the_start_screen_removes_it_from_the_file_menu_too()
    {
        // Arrange
        (SettingsStore settings, MainWindowViewModel vm) = New(fileMenuLimit: 3, startScreenLimit: 10, files: 15);
        IRecentFilesMenu menu = vm;
        string[] all = settings.RecentFiles.ToArray();
        int changes = 0;
        menu.Changed += (_, _) => changes++;

        // Act
        vm.StartScreen.RecentFileItems[0].RemoveCommand.Execute(null);

        // Assert
        settings.RecentFiles.Should().Equal(all.Skip(1));
        menu.RecentFiles.Should().Equal(all.Skip(1).Take(3));
        vm.StartScreen.RecentFileItems.Select(f => f.Path).Should().Equal(all.Skip(1).Take(10));
        changes.Should().BeGreaterThan(0, "the File menu is rebuilt");
    }

    [Fact]
    public void Removing_an_entry_from_the_file_menu_removes_it_from_the_start_screen_too()
    {
        // Arrange
        (SettingsStore settings, MainWindowViewModel vm) = New(fileMenuLimit: 3, startScreenLimit: 10, files: 15);
        IRecentFilesMenu menu = vm;
        string[] all = settings.RecentFiles.ToArray();

        // Act
        menu.Remove(all[1]);

        // Assert
        vm.StartScreen.RecentFileItems.Select(f => f.Path).Should().Equal(all.Where((_, i) => i != 1).Take(10));
        menu.RecentFiles.Should().Equal(all.Where((_, i) => i != 1).Take(3));
    }

    [Fact]
    public void The_start_screen_follows_its_limit_setting()
    {
        // Arrange
        (SettingsStore settings, MainWindowViewModel vm) = New(fileMenuLimit: 3, startScreenLimit: 10, files: 15);

        // Act
        settings.StartScreenRecentFilesLimit = 5;

        // Assert
        vm.StartScreen.RecentFileItems.Should().HaveCount(5);

        // Act
        settings.StartScreenRecentFilesLimit = 0;

        // Assert
        vm.StartScreen.HasRecentFiles.Should().BeFalse();
    }
}
