using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;

namespace Signet.App.UiTests;

/// <summary>
/// Signet's TreeDataGrid infrastructure: localized column headers (<see cref="LocalizedColumns{TModel}"/>), headers
/// never cut off (<see cref="TreeDataGridHeaderSizing"/>), resizable columns and the <c>gridLines</c> style class.
/// </summary>
public sealed class TreeDataGridInfrastructureTests
{
    private sealed record Item(string Name);

    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    private static (Window Window, TreeDataGrid Grid, FlatTreeDataGridSource<Item> Source, LocalizedColumns<Item> Columns) Show(
        GridLength width, string? classes = null)
    {
        LocalizedColumns<Item> columns = new();
        FlatTreeDataGridSource<Item> source = new(new ObservableCollection<Item> { new("a"), new("b") })
        {
            Columns = { columns.Text("ValidationResultsView_Message", x => x.Name, width) },
        };
        TreeDataGrid grid = new() { Source = source };
        if (classes is not null)
        {
            grid.Classes.Add(classes);
        }

        Window window = new() { Width = 500, Height = 300, Content = grid };
        window.Show();
        Settle(window);
        return (window, grid, source, columns);
    }

    [AvaloniaFact]
    public void A_narrow_column_is_widened_to_its_header_and_columns_are_resizable()
    {
        (Window window, TreeDataGrid grid, FlatTreeDataGridSource<Item> source, _) = Show(new GridLength(5));

        grid.CanUserResizeColumns.Should().BeTrue();
        TreeDataGridColumnHeader header = grid.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Single();
        header.Measure(Size.Infinity);
        source.Columns[0].ActualWidth.Should().BeGreaterThanOrEqualTo(header.DesiredSize.Width, "the header must be fully visible");
        window.Close();
    }

    [AvaloniaFact]
    public void Column_headers_follow_a_language_switch()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            (Window window, _, FlatTreeDataGridSource<Item> source, LocalizedColumns<Item> columns) = Show(new GridLength(1, GridUnitType.Star));
            source.Columns[0].Header.Should().Be(Strings.Get("ValidationResultsView_Message"));
            string english = Strings.Get("ValidationResultsView_Message");

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pl");
            Strings.NotifyLanguageChanged();

            source.Columns[0].Header.Should().Be(Strings.Get("ValidationResultsView_Message")).And.NotBe(english);
            columns.Should().NotBeNull("the factory must stay alive for the language listener");
            window.Close();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            Strings.NotifyLanguageChanged();
        }
    }

    [AvaloniaFact]
    public void The_gridLines_class_draws_a_line_under_every_row()
    {
        (Window window, TreeDataGrid grid, _, _) = Show(new GridLength(1, GridUnitType.Star), "gridLines");

        TreeDataGridRow[] rows = grid.GetVisualDescendants().OfType<TreeDataGridRow>().ToArray();
        rows.Should().HaveCount(2).And.OnlyContain(r => r.BorderThickness == new Thickness(0, 0, 0, 1) && r.BorderBrush is ISolidColorBrush);
        window.Close();
    }
}
