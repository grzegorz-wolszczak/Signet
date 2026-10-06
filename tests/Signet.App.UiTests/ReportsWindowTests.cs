using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.BookManipulation;

namespace Signet.App.UiTests;

/// <summary>
/// The "Reports" window: every report is a TreeDataGrid with resizable columns whose headers are never cut off, and a
/// double click navigates to the row's file.
/// </summary>
public sealed class ReportsWindowTests
{
    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    [AvaloniaFact]
    public void Report_columns_are_resizable_and_no_header_is_cut_off()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        ReportsViewModel vm = new(book);
        // Wide enough for all the columns of every report: TreeDataGrid realizes only the columns in view (the
        // headless font is wide).
        ReportsWindow window = new() { DataContext = vm, Width = 2400, Height = 500 };
        window.Show();

        // Every tab, so that each grid gets loaded.
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.ItemCount.Should().Be(9);
        for (int i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            Settle(window);

            TreeDataGrid grid = window.GetVisualDescendants().OfType<TreeDataGrid>().First(g => g.IsEffectivelyVisible);
            grid.CanUserResizeColumns.Should().BeTrue();
            grid.Classes.Should().Contain("gridLines");
            TreeDataGridColumnHeader[] headers = grid.GetVisualDescendants().OfType<TreeDataGridColumnHeader>()
                .Where(h => h.IsVisible).ToArray();
            headers.Should().HaveCount(grid.Columns!.Count, $"every column of tab {i} fits the wide window");
            foreach (TreeDataGridColumnHeader header in headers)
            {
                header.Measure(Size.Infinity);
                grid.Columns[header.ColumnIndex].ActualWidth.Should().BeGreaterThanOrEqualTo(
                    header.DesiredSize.Width, $"the header \"{header.Header}\" (tab {i}) must be fully visible");
            }
        }

        window.Close();
    }

    [AvaloniaFact]
    public void All_files_lists_the_book_files_and_a_double_click_opens_one()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        ReportsViewModel vm = new(book);
        string? navigatedTo = null;
        vm.NavigationRequested += (path, _) => navigatedTo = path;
        ReportsWindow window = new() { DataContext = vm, Width = 2400, Height = 500 };
        window.Show();
        Settle(window);
        TreeDataGrid grid = window.GetVisualDescendants().OfType<TreeDataGrid>().First(g => g.IsEffectivelyVisible);
        grid.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
            Strings.Get("ReportsWindow_Name"), Strings.Get("ReportsWindow_Type"),
            Strings.Get("ReportsWindow_SizeBytes"), Strings.Get("ReportsWindow_InSpine"));
        grid.GetVisualDescendants().OfType<TreeDataGridCheckBoxCell>().Should().NotBeEmpty()
            .And.OnlyContain(c => c.IsReadOnly, "report check boxes are read-only");
        Control cell = grid.TryGetCell(0, 0)!;
        Point point = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle(window);

        navigatedTo.Should().Be(vm.AllFiles[0].BookPath);
        window.Close();
    }
}
