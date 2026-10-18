using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>
/// Toolbar areas of the main window: the grips, the empty bottom area, and mapping a pointer position over the real
/// rows to a drop target. The drag itself (the OS drag loop) is not driven here.
/// </summary>
public sealed class ToolbarAreaViewTests
{
    private static (MainWindow Window, MainWindowViewModel Vm) ShowMainWindow()
    {
        MainWindowViewModel vm = new();
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
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();

        ToolbarStripView[] strips = Area(window, vm.TopToolbars).GetVisualDescendants().OfType<ToolbarStripView>()
            .Where(s => s.IsVisible).ToArray();

        strips.Should().NotBeEmpty();
        strips.Should().OnlyContain(s => s.FindControl<Border>("Grip")!.Bounds.Width >= 8);
    }

    [AvaloniaFact]
    public void The_empty_bottom_area_takes_no_space_until_a_toolbar_is_dragged()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarAreaView bottom = Area(window, vm.BottomToolbars);
        bottom.IsVisible.Should().BeFalse();

        ToolbarDragState.Begin(ToolbarId.Format);
        try
        {
            Flush(window);

            bottom.IsVisible.Should().BeTrue();
            bottom.FindControl<Border>("Placeholder")!.IsVisible.Should().BeTrue();
            bottom.ResolveAt(new Point(10, 5)).Should().Match<ToolbarDropTarget>(t => t.NewRow && t.RowIndex == 0);
        }
        finally
        {
            ToolbarDragState.End();
        }

        Flush(window);
        bottom.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_toolbar_dropped_in_a_new_bottom_row_shows_above_the_status_bar()
    {
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();

        vm.BottomToolbars.Drop(ToolbarId.Format, new ToolbarDropTarget(true, 0, null, default));
        Flush(window);

        ToolbarAreaView top = Area(window, vm.TopToolbars);
        ToolbarAreaView bottom = Area(window, vm.BottomToolbars);
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
        (MainWindow window, MainWindowViewModel vm) = ShowMainWindow();
        ToolbarAreaView top = Area(window, vm.TopToolbars);
        ToolbarStripView file = Strip(top, ToolbarId.File);
        Point filePos = file.TranslatePoint(default, top)!.Value;
        Size fileSize = file.Bounds.Size;

        // Left half of the File toolbar, vertically in the middle of its row → before File, in the first row.
        ToolbarDropTarget before = top.ResolveAt(new Point(filePos.X + 2, filePos.Y + fileSize.Height / 2));
        before.NewRow.Should().BeFalse();
        before.RowIndex.Should().Be(0);
        before.Before.Should().Be(ToolbarId.File);

        // Right of everything in the first row → the end of that row.
        ToolbarDropTarget end = top.ResolveAt(new Point(top.Bounds.Width - 1, filePos.Y + fileSize.Height / 2));
        end.NewRow.Should().BeFalse();
        end.RowIndex.Should().Be(0);

        // Above the first row and below the last → new rows at the ends.
        top.ResolveAt(new Point(20, 0)).Should().Match<ToolbarDropTarget>(t => t.NewRow && t.RowIndex == 0);
        ToolbarDropTarget last = top.ResolveAt(new Point(20, top.Bounds.Height - 0.5));
        last.NewRow.Should().BeTrue();
        last.RowIndex.Should().BeGreaterThan(0);
    }
}
