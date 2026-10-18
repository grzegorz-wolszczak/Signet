using System;
using System.IO;
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

namespace Signet.App.UiTests;

/// <summary>The icon column of the lists in the "Customize Toolbars" window.</summary>
public sealed class ToolbarCustomizeWindowTests
{
    private static ToolbarCustomizeWindow Show(bool withEntryWithoutIcon = false)
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), "Signet.Tests", $"uitests-{Guid.NewGuid():N}.xml");
        SettingsStore settings = new(settingsPath);
        new IconThemeManager(settings, NullLogger<IconThemeManager>.Instance).ApplySaved();

        ToolbarCustomizeViewModel vm = new(new ToolbarManager(settings), new MainWindowViewModel().Actions);
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
        ToolbarCustomizeWindow window = Show();

        Image[] icons = window.GetVisualDescendants().OfType<Image>()
            .Where(i => i.DataContext is ToolbarEntry { IconKey: not null } && i.IsEffectivelyVisible)
            .ToArray();

        icons.Should().NotBeEmpty("the default layout holds actions with icons (e.g. the Tools toolbar)");
        icons.Should().OnlyContain(i => i.Bounds.Width > 0 && i.Bounds.Height > 0);
    }

    [AvaloniaFact]
    public void The_text_of_every_entry_starts_at_the_same_place_whether_or_not_it_has_an_icon()
    {
        ToolbarCustomizeWindow window = Show(withEntryWithoutIcon: true);
        ListBox available = window.GetVisualDescendants().OfType<ListBox>().First();

        double[] textStarts = available.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.DataContext is ToolbarEntry && t.IsEffectivelyVisible)
            .Select(t => t.TranslatePoint(default, available)!.Value.X)
            .Distinct()
            .ToArray();

        textStarts.Should().HaveCount(1);
        window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.DataContext is ToolbarEntry { IconKey: null } && t.IsEffectivelyVisible)
            .Should().NotBeEmpty("some actions have no icon — they must keep the empty icon column");
    }

    [AvaloniaFact]
    public void The_menu_buttons_of_the_Heading_and_Change_Case_toolbars_have_their_icons_in_the_list()
    {
        MainWindowViewModel vm = new();
        ToolbarCustomizeViewModel sut = new(
            new ToolbarManager(new SettingsStore(Path.Combine(Path.GetTempPath(), "Signet.Tests", $"uitests-{Guid.NewGuid():N}.xml"))),
            vm.Actions);

        sut.AvailableActions.Single(e => e.Id == ToolbarManager.HeadingsMenu).IconKey.Should().Be("heading-all");
        sut.AvailableActions.Single(e => e.Id == ToolbarManager.CaseMenu).IconKey.Should().Be("case-change");
        sut.AvailableActions.Single(e => e.Id == AppActionIds.Save).IconKey.Should().NotBeNullOrEmpty();
    }
}
