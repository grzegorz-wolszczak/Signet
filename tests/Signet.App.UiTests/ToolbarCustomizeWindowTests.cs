using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The icon column of the lists in the "Customize Toolbars" window.</summary>
public sealed class ToolbarCustomizeWindowTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private ToolbarCustomizeWindow Show(bool withEntryWithoutIcon = false)
    {
        string settingsPath = _disposables.Add(new TempDir("uitests-")).Combine("file.xml");
        SettingsStore settings = new(settingsPath);
        new IconThemeManager(settings, NullLogger<IconThemeManager>.Instance).ApplySaved();

        ToolbarCustomizeViewModel vm = new(new ToolbarManager(settings), _disposables.Add(new MainWindowViewModel()).Actions);
        if (withEntryWithoutIcon)
        {
            // Every command of the catalog has an icon now; an action added later (or a clip slot) may not.
            vm.AvailableActions.Insert(0, new ToolbarEntry("test.without-icon", "Without an icon"));
        }

        ToolbarCustomizeWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Entries_with_an_icon_show_it_in_a_column_in_front_of_the_text()
    {
        // Act
        ToolbarCustomizeWindow window = Show();

        Image[] icons = window.GetVisualDescendants().OfType<Image>()
            .Where(i => i.DataContext is ToolbarEntry { IconKey: not null } && i.IsEffectivelyVisible)
            .ToArray();

        // Assert
        icons.Should().NotBeEmpty("the default layout holds actions with icons (e.g. the Tools toolbar)");
        icons.Should().OnlyContain(
            i => System.Math.Abs(i.Bounds.Width - Signet.Core.Misc.ToolbarAppearance.BigIconPixels) < 0.01
                && System.Math.Abs(i.Bounds.Height - Signet.Core.Misc.ToolbarAppearance.BigIconPixels) < 0.01,
            "the icons show at the biggest toolbar size, as they can look on a toolbar");
    }

    [AvaloniaFact]
    public void The_text_of_every_entry_starts_at_the_same_place_whether_or_not_it_has_an_icon()
    {
        // Act
        ToolbarCustomizeWindow window = Show(withEntryWithoutIcon: true);
        ListBox available = window.GetVisualDescendants().OfType<ListBox>().First();

        double[] textStarts = available.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.DataContext is ToolbarEntry && t.IsEffectivelyVisible)
            .Select(t => t.TranslatePoint(default, available)!.Value.X)
            .Distinct()
            .ToArray();

        // Assert
        textStarts.Should().HaveCount(1);
        window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.DataContext is ToolbarEntry { IconKey: null } && t.IsEffectivelyVisible)
            .Should().NotBeEmpty("some actions have no icon — they must keep the empty icon column");
    }

    [AvaloniaFact]
    public void The_menu_buttons_of_the_Heading_and_Change_Case_toolbars_have_their_icons_in_the_list()
    {
        // Arrange
        MainWindowViewModel vm = _disposables.Add(new MainWindowViewModel());

        // Act
        ToolbarCustomizeViewModel sut = new(
            new ToolbarManager(new SettingsStore(_disposables.Add(new TempDir("uitests-")).Combine("file.xml"))),
            vm.Actions);

        // Assert
        sut.AvailableActions.Single(e => e.Id == ToolbarManager.HeadingsMenu).IconKey.Should().Be("heading-all");
        sut.AvailableActions.Single(e => e.Id == ToolbarManager.CaseMenu).IconKey.Should().Be("case-change");
        sut.AvailableActions.Single(e => e.Id == AppActionIds.Save).IconKey.Should().NotBeNullOrEmpty();
    }

    [AvaloniaFact]
    public void Available_actions_filter_by_label_and_clear_restores_all_entries()
    {
        // Arrange
        ToolbarCustomizeWindow window = Show();
        try
        {
            ToolbarCustomizeViewModel vm = (ToolbarCustomizeViewModel)window.DataContext!;
            ToolbarEntry entry = new("test.unique", "Unique test action");
            vm.AvailableActions.Add(entry);
            int unfilteredCount = vm.AvailableActions.Count;
            TextBox searchBox = window.GetVisualDescendants().OfType<TextBox>()
                .Single(textBox => !string.IsNullOrEmpty(textBox.PlaceholderText));
            ListBox availableActionsList = window.GetVisualDescendants().OfType<ListBox>().First();
            Button clearButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "✕"));

            // Act
            searchBox.Text = "UNIQUE TEST";
            Dispatcher.UIThread.RunJobs();

            // Assert
            vm.SearchText.Should().Be("UNIQUE TEST");
            vm.FilteredAvailableActions.Should().ContainSingle().Which.Should().BeSameAs(entry);
            availableActionsList.Items.OfType<ToolbarEntry>().Should().ContainSingle().Which.Should().BeSameAs(entry);
            clearButton.IsEffectivelyVisible.Should().BeTrue();

            // Act
            clearButton.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // Assert
            vm.SearchText.Should().BeEmpty();
            vm.FilteredAvailableActions.Should().HaveCount(unfilteredCount);
            availableActionsList.Items.OfType<ToolbarEntry>().Should().HaveCount(unfilteredCount);
            clearButton.IsEffectivelyVisible.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }
}
