using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.App.Views;
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
    public void Right_click_menu_of_a_toolbar_offers_hide_and_edit_bound_to_that_toolbar()
    {
        // Arrange
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarStripView format = Strip(Area(window, vm.TopToolbars), ToolbarId.Format);

        ContextMenu menu = format.ContextMenu!;

        menu.Should().NotBeNull();
        menu.DataContext = format.DataContext;
        MenuItem[] items = menu.Items.OfType<MenuItem>().ToArray();
        items.Should().HaveCount(2);

        ToolbarId? edited = null;
        vm.CustomizeToolbarRequested += (_, id) => edited = id;

        // Act
        items[1].Command!.Execute(null);

        // Assert
        edited.Should().Be(ToolbarId.Format);

        // Act
        items[0].Command!.Execute(null);
        Flush(window);

        // Assert
        format.IsVisible.Should().BeFalse();
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
