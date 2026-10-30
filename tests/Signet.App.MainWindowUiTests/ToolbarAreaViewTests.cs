using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.MainWindowUiTests;

/// <summary>
/// Toolbar areas of the main window: the grips, the empty bottom area, and mapping a pointer position over the real
/// rows to a drop target. The drag itself (the OS drag loop) is not driven here.
/// </summary>
public sealed class ToolbarAreaViewTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (MainWindow Window, MainWindowViewModel Vm) ShowMainWindow()
    {
        MainWindowViewModel vm = _disposables.Add(new MainWindowViewModel());
        MainWindow window = new() { DataContext = vm };
        window.Show();
        Flush(window);
        return (window, vm);
    }

    private static void Flush(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static ToolbarAreaView Area(MainWindow window, ToolbarAreaViewModel vm) =>
        window.GetVisualDescendants().OfType<ToolbarAreaView>().Single(a => ReferenceEquals(a.DataContext, vm));

    private static ToolbarStripView Strip(ToolbarAreaView area, ToolbarId id) =>
        area.GetVisualDescendants().OfType<ToolbarStripView>().Single(s => s.DataContext is ToolbarViewModel t && t.Id == id);

    [AvaloniaFact]
    public void Every_shown_toolbar_has_a_grip()
    {
        // Act
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();

        ToolbarStripView[] strips = Area(window, vm.TopToolbars).GetVisualDescendants().OfType<ToolbarStripView>()
            .Where(s => s.IsVisible).ToArray();

        // Assert
        strips.Should().NotBeEmpty();
        strips.Should().OnlyContain(s => s.FindControl<Border>("Grip")!.Bounds.Width >= 8);
    }

    [AvaloniaFact]
    public void The_empty_bottom_area_takes_no_space_until_a_toolbar_is_dragged()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarAreaView bottom = Area(window, vm.BottomToolbars);

        bottom.IsVisible.Should().BeFalse();

        // Act
        ToolbarDragState.Begin(ToolbarId.Format);
        try
        {
            Flush(window);

            // Assert
            bottom.IsVisible.Should().BeTrue();
            bottom.FindControl<Border>("Placeholder")!.IsVisible.Should().BeTrue();

            // Act & Assert
            bottom.ResolveAt(new Point(10, 5)).Should().Match<ToolbarDropTarget>(t => t.NewRow && t.RowIndex == 0);
        }
        finally
        {
            // Act
            ToolbarDragState.End();
        }

        Flush(window);

        // Assert
        bottom.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_toolbar_dropped_in_a_new_bottom_row_shows_above_the_status_bar()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();

        // Act
        vm.BottomToolbars.Drop(ToolbarId.Format, new ToolbarDropTarget(true, 0, null, default));
        Flush(window);

        ToolbarAreaView top = Area(window, vm.TopToolbars);
        ToolbarAreaView bottom = Area(window, vm.BottomToolbars);

        // Assert
        bottom.IsVisible.Should().BeTrue();
        top.GetVisualDescendants().OfType<ToolbarStripView>().Select(s => ((ToolbarViewModel)s.DataContext!).Id)
            .Should().NotContain(ToolbarId.Format);
        ToolbarStripView moved = Strip(bottom, ToolbarId.Format);
        moved.IsVisible.Should().BeTrue();

        TextBlock bookFile = window.FindControl<TextBlock>("BookFileText")!;
        double bottomEdge = bottom.TranslatePoint(new Point(0, bottom.Bounds.Height), window)!.Value.Y;
        double statusTop = bookFile.TranslatePoint(default, window)!.Value.Y - 3; // the status bar's own padding
        double topEdge = top.TranslatePoint(new Point(0, top.Bounds.Height), window)!.Value.Y;
        bottomEdge.Should().BeLessThanOrEqualTo(statusTop + 0.5, "the bottom toolbars sit above the status bar");
        bottom.TranslatePoint(default, window)!.Value.Y.Should().BeGreaterThan(topEdge, "and below the top toolbars");
    }

    [AvaloniaFact]
    public void The_pointer_over_the_rows_resolves_to_the_row_the_toolbar_under_it_and_the_gaps()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarAreaView top = Area(window, vm.TopToolbars);
        ToolbarStripView file = Strip(top, ToolbarId.File);
        Point filePos = file.TranslatePoint(default, top)!.Value;
        Size fileSize = file.Bounds.Size;

        // Left half of the File toolbar, vertically in the middle of its row → before File, in the first row.
        // Act
        ToolbarDropTarget before = top.ResolveAt(new Point(filePos.X + 2, filePos.Y + fileSize.Height / 2));

        // Assert
        before.NewRow.Should().BeFalse();
        before.RowIndex.Should().Be(0);
        before.Before.Should().Be(ToolbarId.File);

        // Right of everything in the first row → the end of that row.
        // Act
        ToolbarDropTarget end = top.ResolveAt(new Point(top.Bounds.Width - 1, filePos.Y + fileSize.Height / 2));

        // Assert
        end.NewRow.Should().BeFalse();
        end.RowIndex.Should().Be(0);

        // Above the first row and below the last → new rows at the ends.
        // Act & Assert
        top.ResolveAt(new Point(20, 0)).Should().Match<ToolbarDropTarget>(t => t.NewRow && t.RowIndex == 0);

        // Act
        ToolbarDropTarget last = top.ResolveAt(new Point(20, top.Bounds.Height - 0.5));

        // Assert
        last.NewRow.Should().BeTrue();
        last.RowIndex.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public void Right_click_menu_of_a_toolbar_offers_hide_and_the_toolbar_settings_bound_to_that_toolbar()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarStripView format = Strip(Area(window, vm.TopToolbars), ToolbarId.Format);

        ContextMenu menu = format.ContextMenu!;

        menu.Should().NotBeNull();
        menu.DataContext = format.DataContext;
        menu.Open(format);
        Dispatcher.UIThread.RunJobs();
        MenuItem title = menu.Items.OfType<MenuItem>().First();
        title.Classes.Should().Contain(MenuTitle.TitleClass);
        title.Header.Should().Be(ToolbarManager.DisplayName(ToolbarId.Format), "the title names the toolbar");
        menu.Items[1].Should().BeOfType<Separator>();
        menu.Close();
        menu.Items.OfType<Control>().Skip(2).Select(c => c.Name).Should().Equal(
            "HideItem", "SettingsSeparator", "TextPositionItem", "IconSizeItem", "ConfigureItem", "LockItem");
        menu.Items[3].Should().BeOfType<Separator>("the settings are set apart by a plain line");
        MenuItem[] items = menu.Items.OfType<MenuItem>().Skip(1).ToArray();
        items[1].Items.OfType<MenuItem>().Should().HaveCount(4);
        items[2].Items.OfType<MenuItem>().Should().HaveCount(3);
        items[3].Tag.Should().Be(ActionIconKeys.ToolbarConfigure);
        items[4].Tag.Should().Be(ActionIconKeys.Lock);

        ToolbarId? configured = null;
        vm.CustomizeToolbarRequested += (_, id) => configured = id;

        // Act
        items[3].Command!.Execute(null);

        // Assert
        configured.Should().Be(ToolbarId.Format, "the window opens on the toolbar the menu was opened on");

        // Act
        items[0].Command!.Execute(null);
        Flush(window);

        // Assert
        format.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Right_click_on_the_empty_part_of_the_area_opens_the_Toolbars_menu_under_a_title()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        window.Width = 2400;
        Flush(window);
        ToolbarAreaView top = Area(window, vm.TopToolbars);
        ToolbarStripView format = Strip(top, ToolbarId.Format);
        Point empty = top.TranslatePoint(new Point(top.Bounds.Width - 10, 8), window)!.Value;

        // Act
        window.MouseDown(empty, MouseButton.Right);
        window.MouseUp(empty, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        // Assert
        ContextMenu menu = top.ContextMenu!;
        menu.IsOpen.Should().BeTrue();
        object[] items = menu.Items.Cast<object>().ToArray();
        MenuItem title = items[0].Should().BeOfType<MenuItem>().Subject;
        title.Header.Should().Be(Strings.Get("Menu_Toolbars").Replace("&", string.Empty, StringComparison.Ordinal));
        title.FontWeight.Should().Be(FontWeight.Bold);
        title.IsHitTestVisible.Should().BeFalse("the title is not an item to click");
        items[1].Should().BeOfType<Separator>();
        items.Skip(2).Should().Equal(vm.ToolbarsMenuItems, "the same items as View › Toolbars");
        MenuItem formatToggle = menu.GetLogicalChildren().OfType<MenuItem>()
            .Single(m => m.DataContext is Signet.App.Menu.MenuItemViewModel { Header: var h } && h == ToolbarManager.DisplayName(ToolbarId.Format));
        formatToggle.IsChecked.Should().BeTrue("the items take the style of the main menu");
        formatToggle.StaysOpenOnClick.Should().BeTrue();

        // Act
        menu.Close();
        Dispatcher.UIThread.RunJobs();
        Point onToolbar = format.TranslatePoint(new Point(format.Bounds.Width / 2, format.Bounds.Height / 2), window)!.Value;
        window.MouseDown(onToolbar, MouseButton.Right);
        window.MouseUp(onToolbar, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        // Assert
        format.ContextMenu!.IsOpen.Should().BeTrue("on a toolbar its own menu opens");
        menu.IsOpen.Should().BeFalse();
        format.ContextMenu.Close();
    }

    [AvaloniaFact]
    public void Hide_all_toolbars_takes_the_toolbar_areas_out_of_the_window_and_back()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarAreaView top = Area(window, vm.TopToolbars);
        static bool HoldsToolbarToggles(Signet.App.Menu.MenuItemViewModel item) =>
            item.Items is { } children && children.Any(t => t.Header == ToolbarManager.DisplayName(ToolbarId.Format));
        MenuItem viewMenu = window.FindControl<Avalonia.Controls.Menu>("MainMenu")!.GetLogicalChildren().OfType<MenuItem>()
            .Single(m => m.DataContext is Signet.App.Menu.MenuItemViewModel { Items: { } items } && items.Any(HoldsToolbarToggles));
        viewMenu.Open();
        Flush(window);
        MenuItem toolbarsMenu = viewMenu.GetLogicalChildren().OfType<MenuItem>()
            .Single(m => m.DataContext is Signet.App.Menu.MenuItemViewModel item && HoldsToolbarToggles(item));
        toolbarsMenu.Open();
        Flush(window);
        MenuItem[] entries = toolbarsMenu.GetLogicalChildren().OfType<MenuItem>().ToArray();
        MenuItem format = entries.Single(m => m.DataContext is Signet.App.Menu.MenuItemViewModel { Header: var h } && h == ToolbarManager.DisplayName(ToolbarId.Format));
        MenuItem hideAll = entries.Last();

        // Assert
        top.IsVisible.Should().BeTrue();
        format.StaysOpenOnClick.Should().BeTrue("the menu stays open while toolbars are switched");
        hideAll.StaysOpenOnClick.Should().BeTrue();

        // Act
        hideAll.Command!.Execute(null);
        Flush(window);

        // Assert
        top.IsVisible.Should().BeFalse("the toolbars take no room in the window");
        Area(window, vm.BottomToolbars).IsVisible.Should().BeFalse();
        format.IsEffectivelyEnabled.Should().BeFalse();
        hideAll.IsChecked.Should().BeTrue();

        // Act
        hideAll.Command!.Execute(null);
        Flush(window);

        // Assert
        top.IsVisible.Should().BeTrue();
        format.IsEffectivelyEnabled.Should().BeTrue();
        viewMenu.Close();
    }

    [AvaloniaFact]
    public void The_text_position_chosen_in_the_menu_shows_on_every_toolbar()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarAreaView top = Area(window, vm.TopToolbars);
        ToolbarViewModel format = (ToolbarViewModel)Strip(top, ToolbarId.Format).DataContext!;
        Button bold = ActionButton(top, AppActionIds.Bold);
        Button open = ActionButton(top, AppActionIds.Open);

        // Assert
        Label(bold).IsEffectivelyVisible.Should().BeFalse("icons only by default");
        Icon(bold).IsEffectivelyVisible.Should().BeTrue();

        // Act
        format.SetTextPositionCommand.Execute(ToolbarTextPosition.TextUnderIcons);
        Flush(window);

        // Assert
        Label(ActionButton(top, AppActionIds.MendCurrentHtml)).Text.Should().Contain("\n", "a long label takes two lines");
        Label(open).IsEffectivelyVisible.Should().BeTrue("the setting is shared by every toolbar");
        Label(open).Text.Should().NotBeEmpty();
        Content(open).Orientation.Should().Be(Orientation.Vertical);
        Icon(open).IsEffectivelyVisible.Should().BeTrue();

        // Act
        format.SetTextPositionCommand.Execute(ToolbarTextPosition.TextBesideIcons);
        Flush(window);

        // Assert
        Content(open).Orientation.Should().Be(Orientation.Horizontal);
        Label(open).IsEffectivelyVisible.Should().BeTrue();

        // Act
        format.SetTextPositionCommand.Execute(ToolbarTextPosition.TextOnly);
        Flush(window);

        // Assert
        Icon(open).IsEffectivelyVisible.Should().BeFalse();
        Label(open).IsEffectivelyVisible.Should().BeTrue();
        RadioItem(Strip(top, ToolbarId.Format), "TextPositionItem", 1).IsChecked.Should().BeTrue();
        RadioItem(Strip(top, ToolbarId.Format), "TextPositionItem", 0).IsChecked.Should().BeFalse();
    }

    [AvaloniaFact]
    public void With_labels_in_one_and_in_two_lines_the_icons_of_a_row_stay_aligned_at_the_top()
    {
        // Arrange
        using TempDir temp = new();
        new IconThemeManager(new SettingsStore(temp.Combine("icon-theme-settings.xml")), NullLogger<IconThemeManager>.Instance).ApplySaved();
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        window.Width = 2400;
        ToolbarAreaView top = Area(window, vm.TopToolbars);
        ToolbarViewModel format = (ToolbarViewModel)Strip(top, ToolbarId.Format).DataContext!;

        // Act
        format.SetTextPositionCommand.Execute(ToolbarTextPosition.TextUnderIcons);
        Flush(window);
        Button open = ActionButton(top, AppActionIds.Open);
        Button mend = ActionButton(top, AppActionIds.MendCurrentHtml);

        // Assert
        Label(open).Text.Should().NotContain("\n", "the premise: a label in one line");
        Label(mend).Text.Should().Contain("\n", "the premise: a label in two lines");
        double TopOf(Button button) => DrawnIcon(button).TranslatePoint(default, window)!.Value.Y;
        TopOf(open).Should().BeApproximately(TopOf(mend), 0.5);

        // Act
        format.SetTextPositionCommand.Execute(ToolbarTextPosition.TextBesideIcons);
        Flush(window);

        // Assert
        TopOf(open).Should().BeApproximately(TopOf(mend), 0.5);
    }

    [AvaloniaFact]
    public void The_icon_size_chosen_in_the_menu_resizes_the_icons_of_every_toolbar()
    {
        // Arrange
        using TempDir temp = new();
        new IconThemeManager(new SettingsStore(temp.Combine("icon-theme-settings.xml")), NullLogger<IconThemeManager>.Instance).ApplySaved();
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarAreaView top = Area(window, vm.TopToolbars);
        ToolbarViewModel format = (ToolbarViewModel)Strip(top, ToolbarId.Format).DataContext!;
        Button open = ActionButton(top, AppActionIds.Open);

        // Assert
        DrawnIcon(open).Bounds.Width.Should().Be(24);

        // Act
        format.SetIconSizeCommand.Execute(ToolbarIconSize.Small);
        Flush(window);

        // Assert
        DrawnIcon(open).Bounds.Width.Should().Be(16);
        RadioItem(Strip(top, ToolbarId.Format), "IconSizeItem", 0).IsChecked.Should().BeTrue();
        RadioItem(Strip(top, ToolbarId.Format), "IconSizeItem", 1).IsChecked.Should().BeFalse();
        RadioItem(Strip(top, ToolbarId.Format), "IconSizeItem", 2).IsChecked.Should().BeFalse();

        // Act
        format.SetIconSizeCommand.Execute(ToolbarIconSize.Big);
        Flush(window);

        // Assert
        DrawnIcon(open).Bounds.Width.Should().Be(32);
        RadioItem(Strip(top, ToolbarId.Format), "IconSizeItem", 2).IsChecked.Should().BeTrue();
        RadioItem(Strip(top, ToolbarId.Format), "IconSizeItem", 0).IsChecked.Should().BeFalse();

        // Act
        format.SetIconSizeCommand.Execute(ToolbarIconSize.Medium);
        Flush(window);

        // Assert
        DrawnIcon(open).Bounds.Width.Should().Be(24);
        RadioItem(Strip(top, ToolbarId.Format), "IconSizeItem", 1).IsChecked.Should().BeTrue("the radio follows a change made again");
    }

    [AvaloniaFact]
    public void Locking_the_positions_hides_every_grip_and_marks_the_lock_item()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarAreaView top = Area(window, vm.TopToolbars);
        ToolbarStripView formatStrip = Strip(top, ToolbarId.Format);
        ToolbarViewModel format = (ToolbarViewModel)formatStrip.DataContext!;
        MenuItem lockItem = formatStrip.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Name == "LockItem");
        lockItem.DataContext = format;

        // Act
        format.TogglePositionsLockedCommand.Execute(null);
        Flush(window);

        // Assert
        Grips(top).Should().OnlyContain(g => !g.IsVisible);
        lockItem.Classes.Should().Contain("on");

        // Act
        format.TogglePositionsLockedCommand.Execute(null);
        Flush(window);

        // Assert
        Grips(top).Should().OnlyContain(g => g.IsVisible);
        lockItem.Classes.Should().NotContain("on");
    }

    [AvaloniaFact]
    public void The_font_of_the_button_texts_follows_the_settings()
    {
        // Arrange
        using TempDir temp = new();
        MainWindowViewModel actions = _disposables.Add(new MainWindowViewModel());
        SettingsStore settings = new(temp.Combine("settings.xml"));
        ToolbarManager manager = new(settings);
        ToolbarViewModel file = new(ToolbarId.File, manager, actions.Actions);
        manager.AppearanceChanged += (_, _) => file.RefreshAppearance();
        Window window = new() { Width = 600, Height = 100, Content = new ToolbarStripView { DataContext = file } };
        window.Show();
        Button open = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.DataContext is ToolbarItemViewModel { Action: { } a } && a.Id == AppActionIds.Open);

        System.IO.File.WriteAllText("/tmp/claude-1000/-home-gregw-repos-my-github-Signet/2bce09ea-53ad-4cc6-9b74-f5985c828d1b/scratchpad/dbg.txt",
            $"label size={Label(open).FontSize} fam={Label(open).FontFamily} w={Label(open).FontWeight}; button size={open.FontSize} fam={open.FontFamily}");
        // Act
        settings.ToolbarAppearance = settings.ToolbarAppearance with
        {
            TextPosition = ToolbarTextPosition.TextBesideIcons,
            FontSize = 17,
            FontWeight = 700,
        };
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        Label(open).IsEffectivelyVisible.Should().BeTrue();
        Label(open).FontSize.Should().Be(17);
        Label(open).FontWeight.Should().Be(FontWeight.Bold);

        // Act
        settings.ToolbarAppearance = settings.ToolbarAppearance with { FontSize = 0, FontWeight = 0 };
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        Label(open).FontSize.Should().Be(open.FontSize, "without a size the text keeps the size of the buttons");
        Label(open).FontFamily.Should().Be(open.FontFamily, "without a family the text keeps the interface font");
        Label(open).FontWeight.Should().Be(open.FontWeight);
        window.Close();
    }

    private static Button ActionButton(ToolbarAreaView area, string actionId) =>
        area.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is ToolbarItemViewModel { Action: { } a } && a.Id == actionId);

    private static TextBlock Label(Button button) =>
        button.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("toolbarLabel"));

    private static Panel Icon(Button button) =>
        button.GetVisualDescendants().OfType<Panel>().First(p => p.Classes.Contains("toolbarIcon"));

    private static StackPanel Content(Button button) =>
        button.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Classes.Contains("toolbarContent"));

    private static Control DrawnIcon(Button button) =>
        Icon(button).Children.Single(c => c.IsVisible);

    private static IEnumerable<Border> Grips(ToolbarAreaView area) =>
        area.GetVisualDescendants().OfType<ToolbarStripView>().Select(s => s.FindControl<Border>("Grip")!);

    private static MenuItem RadioItem(ToolbarStripView strip, string submenu, int index)
    {
        MenuItem parent = strip.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Name == submenu);
        parent.DataContext = strip.DataContext;
        return parent.Items.OfType<MenuItem>().ElementAt(index);
    }

    [AvaloniaFact]
    public void Right_click_on_a_disabled_toolbar_button_still_opens_the_toolbar_menu()
    {
        // Without a book the book actions are blocked and their toolbar buttons disabled.
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        vm.Actions.Get(AppActionIds.Cut)!.IsBlocked = true;

        // Act
        Flush(window);
        ToolbarStripView strip = Strip(Area(window, vm.TopToolbars), ToolbarId.Edit);
        Button cut = strip.GetVisualDescendants().OfType<Button>()
            .First(b => b.DataContext is ToolbarItemViewModel { Action.Id: AppActionIds.Cut });

        // Assert
        cut.IsEffectivelyEnabled.Should().BeFalse("the premise: the button is disabled");

        // Control: the same gesture on the (always enabled) grip opens the menu, so the simulation itself works.
        Border grip = strip.FindControl<Border>("Grip")!;
        Point gripCenter = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), window)!.Value;

        // Act
        window.MouseDown(gripCenter, MouseButton.Right);
        window.MouseUp(gripCenter, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        // Assert
        strip.ContextMenu!.IsOpen.Should().BeTrue();

        // Act
        strip.ContextMenu.Close();
        Dispatcher.UIThread.RunJobs();

        Point center = cut.TranslatePoint(new Point(cut.Bounds.Width / 2, cut.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Right);
        window.MouseUp(center, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        // Assert
        strip.ContextMenu!.IsOpen.Should().BeTrue();
        strip.ContextMenu.Close();
    }

    [AvaloniaFact]
    public void Right_click_on_any_button_of_any_toolbar_opens_the_toolbar_menu_when_no_book_is_open()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();

        // Act
        vm.Actions.SetBookOpen(false);
        Flush(window);

        List<string> failures = new();
        int disabled = 0;
        foreach (ToolbarStripView strip in Area(window, vm.TopToolbars).GetVisualDescendants().OfType<ToolbarStripView>().Where(s => s.IsEffectivelyVisible))
        {
            foreach (Control button in strip.GetVisualDescendants().OfType<Control>()
                         .Where(c => c is Button or SplitButton && c.IsEffectivelyVisible && c.Bounds.Width >= 1).ToArray())
            {
                if (!button.IsEffectivelyEnabled)
                {
                    disabled++;
                }

                Point center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(center, MouseButton.Right);
                window.MouseUp(center, MouseButton.Right);
                Dispatcher.UIThread.RunJobs();
                if (strip.ContextMenu!.IsOpen)
                {
                    strip.ContextMenu.Close();
                    Dispatcher.UIThread.RunJobs();
                }
                else
                {
                    failures.Add($"{((ToolbarViewModel)strip.DataContext!).Id}: {button.GetType().Name} {button.DataContext} enabled={button.IsEffectivelyEnabled} at {center}");
                }
            }
        }

        // Assert
        disabled.Should().BeGreaterThan(0, "without a book many toolbar buttons are disabled");
        failures.Should().BeEmpty();
    }
}
