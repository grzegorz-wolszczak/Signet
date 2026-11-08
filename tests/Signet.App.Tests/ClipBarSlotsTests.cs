using System;
using System.IO;
using AwesomeAssertions;
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
using Xunit;

namespace Signet.App.Tests;

/// <summary>How many clips the two Clip Bars show (Preferences › Appearance › Clips).</summary>
public sealed class ClipBarSlotsTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (SettingsStore Settings, MainWindowViewModel Vm) New()
    {
        TempDir temp = _disposables.Add(new TempDir());
        SettingsStore settings = new(temp.Combine("settings.xml"));
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

    private static void AddClips(MainWindowViewModel vm, params string[] names)
    {
        foreach (string name in names)
        {
            vm.Clips.AddEntryCommand.Execute(null);
            vm.Clips.Nodes[^1].Name = name;
        }
    }

    // The label of a slot without a clip is its default text ("Clip N").
    private static string Unassigned(MainWindowViewModel vm, int slot) =>
        vm.Actions.Require(AppActionIds.Clip(slot)).DefaultText.Replace("&", string.Empty, StringComparison.Ordinal);

    [Fact]
    public void By_default_each_bar_holds_thirty_clips()
    {
        // Arrange
        (SettingsStore settings, MainWindowViewModel vm) = New();

        // Act
        AddClips(vm, "A", "B");

        // Assert
        settings.ClipBarSlots.Should().Be(30);
        settings.ClipBar2Slots.Should().Be(30);
        vm.Actions.Require(AppActionIds.Clip(1)).Text.Should().Be("A");
        vm.Actions.Require(AppActionIds.Clip(2)).Text.Should().Be("B");
        vm.Actions.Require(AppActionIds.Clip(31)).Text.Should().Be(Unassigned(vm, 31));
    }

    [Fact]
    public void The_second_bar_continues_where_the_first_one_ends()
    {
        // Arrange
        (SettingsStore settings, MainWindowViewModel vm) = New();
        AddClips(vm, "A", "B", "C", "D");

        // Act
        settings.ClipBarSlots = 2;
        settings.ClipBar2Slots = 1;

        // Assert
        vm.Actions.Require(AppActionIds.Clip(1)).Text.Should().Be("A");
        vm.Actions.Require(AppActionIds.Clip(2)).Text.Should().Be("B");
        vm.Actions.Require(AppActionIds.Clip(3)).Text.Should().Be(Unassigned(vm, 3));
        vm.Actions.Require(AppActionIds.Clip(31)).Text.Should().Be("C");
        vm.Actions.Require(AppActionIds.Clip(32)).Text.Should().Be(Unassigned(vm, 32));
    }

    [Fact]
    public void An_empty_bar_leaves_all_its_slots_without_clips()
    {
        // Arrange
        (SettingsStore settings, MainWindowViewModel vm) = New();
        AddClips(vm, "A", "B");

        // Act
        settings.ClipBarSlots = 0;

        // Assert
        vm.Actions.Require(AppActionIds.Clip(1)).Text.Should().Be(Unassigned(vm, 1));
        vm.Actions.Require(AppActionIds.Clip(31)).Text.Should().Be("A");
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(12, 12)]
    [InlineData(99, 30)]
    public void The_number_of_clips_of_a_bar_is_kept_in_range(int value, int expected)
    {
        // Arrange
        (SettingsStore settings, MainWindowViewModel _) = New();

        // Act
        settings.ClipBarSlots = value;
        settings.ClipBar2Slots = value;

        // Assert
        settings.ClipBarSlots.Should().Be(expected);
        settings.ClipBar2Slots.Should().Be(expected);
    }
}
