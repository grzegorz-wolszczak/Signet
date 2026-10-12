using System;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.Services;
using Signet.App.Tabs;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Opening the file given on the command line.</summary>
public sealed class StartupArgumentsTests
{
    [Fact]
    public void No_arguments_mean_no_file()
    {
        StartupArguments.FileToOpen(Array.Empty<string>(), Path.GetTempPath()).Should().BeNull();
        StartupArguments.FileToOpen(null, Path.GetTempPath()).Should().BeNull();
        StartupArguments.FileToOpen([" "], Path.GetTempPath()).Should().BeNull();
    }

    [Fact]
    public void Relative_path_is_resolved_against_the_current_directory()
    {
        using TempDir temp = new();

        string? path = StartupArguments.FileToOpen(["book.epub", "ignored"], temp.Path);

        path.Should().Be(Path.Combine(temp.Path, "book.epub"));
    }

    [Fact]
    public void Path_with_spaces_and_a_leading_at_sign_is_a_plain_file()
    {
        using TempDir temp = new();
        StartupArguments.FileToOpen(["@my book.epub"], temp.Path)
            .Should().Be(temp.Combine("@my book.epub"), "\"@\" does not denote a response file here");
    }

    [Fact]
    public async Task Startup_opens_the_file_from_the_command_line()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp, "from-cli.epub");
        MainWindowViewModel vm = new();
        vm.AttachFileWorkflowPrompts(new FakeFileWorkflowPrompts());
        vm.StartupFilePath = epub;

        await vm.RunStartupAsync();

        ((IBookWorkspace)vm).CurrentBook.Should().NotBeNull();
        vm.WindowTitle.Should().Contain("from-cli.epub");
        vm.CheckpointHistory.Dispose();
    }

    [Fact]
    public async Task Missing_file_from_the_command_line_shows_an_error_and_starts_without_a_book()
    {
        using TempDir temp = new();
        MainWindowViewModel vm = new();
        FakeFileWorkflowPrompts prompts = new();
        vm.AttachFileWorkflowPrompts(prompts);
        vm.StartupFilePath = temp.Combine("missing.epub");

        await vm.RunStartupAsync();

        prompts.LastError.Should().NotBeNull();
        ((IBookWorkspace)vm).CurrentBook.Should().BeNull("after the error the window starts without a book");
        vm.HasBook.Should().BeFalse();
        vm.StartScreen.IsVisible.Should().BeTrue();
        vm.CheckpointHistory.Dispose();
    }

    // The main window on the given settings (the parameterless constructor uses a settings file of its own).
    private static MainWindowViewModel NewOn(SettingsStore settings)
    {
        StatusBarService statusBar = new();
        KeyboardShortcutManager shortcuts = new(settings);
        MissingDoctypeGuard doctypeGuard = new(settings);
        BookBrowserViewModel bookBrowser = new(settings, statusBar, doctypeGuard);
        PreviewViewModel preview = new(statusBar);
        MainDockFactory dockFactory = new(bookBrowser, preview);
        SpellChecker spellChecker = new(settings);
        MainWindowViewModel vm = new(
            NullLogger<MainWindowViewModel>.Instance,
            new AppActionRegistry(shortcuts, statusBar, NullLogger<AppActionRegistry>.Instance), shortcuts,
            new ToolbarManager(settings), dockFactory, statusBar, settings, bookBrowser,
            new TabManager(dockFactory, statusBar, settings, spellChecker), preview, spellChecker,
            new ClipboardHistoryService(settings), doctypeGuard);
        vm.AttachFileWorkflowPrompts(new FakeFileWorkflowPrompts());
        return vm;
    }

    [Fact]
    public async Task The_last_book_is_reopened_only_when_a_book_was_open_at_exit()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp, "last.epub");
        SettingsStore settings = new(temp.Combine("settings.json"));
        settings.ReopenLastFileOnStartup = true;
        settings.RecentFiles = new[] { epub };

        // Opened and left open at exit: the next start reopens it.
        MainWindowViewModel first = NewOn(settings);
        await first.RunStartupAsync();
        first.HasBook.Should().BeTrue();
        first.PersistState();
        settings.BookOpenAtExit.Should().BeTrue();
        first.CheckpointHistory.Dispose();

        // Closed before exit: the next start has no book.
        MainWindowViewModel second = NewOn(settings);
        await second.RunStartupAsync();
        second.HasBook.Should().BeTrue();
        ((IBookWorkspace)second).CloseBook();
        second.PersistState();
        settings.BookOpenAtExit.Should().BeFalse();
        second.CheckpointHistory.Dispose();

        MainWindowViewModel third = NewOn(settings);
        await third.RunStartupAsync();
        third.HasBook.Should().BeFalse("the application was closed without a book");
        third.CheckpointHistory.Dispose();
    }
}
