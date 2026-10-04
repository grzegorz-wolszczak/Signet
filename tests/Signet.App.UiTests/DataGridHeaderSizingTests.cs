using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.BookManipulation;

namespace Signet.App.UiTests;

/// <summary>Every table: resizable columns, each at least as wide as its header (<see cref="DataGridHeaderSizing"/>).</summary>
public sealed class DataGridHeaderSizingTests
{
    [AvaloniaFact]
    public void Report_columns_are_resizable_and_no_header_is_cut_off()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        ReportsViewModel vm = new(book);
        ReportsWindow window = new() { DataContext = vm, Width = 700, Height = 500 };
        window.Show();

        // Every tab, so that each grid gets loaded.
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        for (int i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();

            DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.IsEffectivelyVisible);
            grid.CanUserResizeColumns.Should().BeTrue();
            foreach (DataGridColumnHeader header in grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is string))
            {
                DataGridColumn column = grid.Columns.Single(c => Equals(c.Header, header.Content));
                header.Measure(Avalonia.Size.Infinity);
                column.ActualWidth.Should().BeGreaterThanOrEqualTo(
                    header.DesiredSize.Width, $"the header \"{header.Content}\" (tab {i}) must be fully visible");
                column.MinWidth.Should().BeGreaterThanOrEqualTo(header.DesiredSize.Width);
            }
        }

        window.Close();
    }
}
