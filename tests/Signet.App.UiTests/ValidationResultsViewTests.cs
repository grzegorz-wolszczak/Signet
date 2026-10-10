using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using System.ComponentModel;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.BookManipulation;

namespace Signet.App.UiTests;

/// <summary>The "Validation Results" panel: the results table (TreeDataGrid) and navigation on double click.</summary>
public sealed class ValidationResultsViewTests
{
    private static readonly string[] SkippedDeadLinks = { "Validation_DeadLink" };

    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    private static ValidationResult Result(string path, int line, string message) =>
        new(ValidationSeverity.Error, path, line, -1, message);

    [AvaloniaFact]
    public void The_table_shows_the_columns_and_one_row_per_result()
    {
        ValidationResultsViewModel vm = new();
        vm.LoadResults(new[] { Result("OEBPS/Text/a.xhtml", 3, "first"), Result("OEBPS/Text/b.xhtml", 0, "second") });
        Window window = new() { Width = 700, Height = 300, Content = new ValidationResultsView { DataContext = vm } };
        window.Show();
        Settle(window);

        TreeDataGrid grid = window.GetVisualDescendants().OfType<TreeDataGrid>().Single();
        grid.IsEffectivelyVisible.Should().BeTrue();
        grid.Classes.Should().Contain("gridLines");
        grid.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
            Strings.Get("ReportsWindow_File"), Strings.Get("ValidationResultsView_Line"),
            Strings.Get("DryRunReplaceWindow_OffsetColumn"), Strings.Get("ValidationResultsView_Fixable"),
            Strings.Get("ValidationResultsView_Message"));
        grid.GetVisualDescendants().OfType<TreeDataGridRow>().Should().HaveCount(2);
        grid.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.Value?.ToString())
            .Should().Contain("first").And.Contain("second").And.Contain("3").And.Contain("N/A");
        window.Close();
    }

    [AvaloniaFact]
    public void Without_results_only_the_message_is_shown()
    {
        ValidationResultsViewModel vm = new();
        Window window = new() { Width = 700, Height = 300, Content = new ValidationResultsView { DataContext = vm } };
        window.Show();
        Settle(window);

        window.GetVisualDescendants().OfType<TreeDataGrid>().Single().IsVisible.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void Double_clicking_a_row_activates_its_result()
    {
        ValidationResultsViewModel vm = new();
        ValidationResult second = Result("OEBPS/Text/b.xhtml", 7, "second");
        vm.LoadResults(new[] { Result("OEBPS/Text/a.xhtml", 3, "first"), second });
        ValidationResult? activated = null;
        vm.EntryActivated += (_, r) => activated = r;
        Window window = new() { Width = 700, Height = 300, Content = new ValidationResultsView { DataContext = vm } };
        window.Show();
        Settle(window);
        TreeDataGrid grid = window.GetVisualDescendants().OfType<TreeDataGrid>().Single();
        Control cell = grid.TryGetCell(3, 1)!;
        Point point = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle(window);

        activated.Should().Be(second);
        window.Close();
    }

    [AvaloniaFact]
    public void Line_and_offset_sort_as_numbers_with_unknown_values_last()
    {
        ValidationResultsViewModel vm = new();
        vm.LoadResults(new[]
        {
            new ValidationResult(ValidationSeverity.Error, "a.xhtml", 10, 5, "ten"),
            new ValidationResult(ValidationSeverity.Error, "a.xhtml", 0, -1, "unknown"),
            new ValidationResult(ValidationSeverity.Error, "a.xhtml", 2, 40, "two"),
        });
        Window window = new() { Width = 700, Height = 300, Content = new ValidationResultsView { DataContext = vm } };
        window.Show();
        Settle(window);
        TreeDataGrid grid = window.GetVisualDescendants().OfType<TreeDataGrid>().Single();
        string[] Messages() => Enumerable.Range(0, grid.Rows!.Count).Select(i => ((ValidationResultRow)grid.Rows[i].Model!).Message).ToArray();

        grid.Source!.SortBy(grid.Columns![1], ListSortDirection.Ascending);
        Messages().Should().Equal("two", "ten", "unknown");

        grid.Source.SortBy(grid.Columns[2], ListSortDirection.Ascending);
        Messages().Should().Equal("ten", "two", "unknown");
        window.Close();
    }

    [AvaloniaFact]
    public void The_toolbar_fixes_the_selected_row_and_knows_whether_there_are_skipped_rules()
    {
        ValidationResultsViewModel vm = new();
        ValidationResult fixable = new(ValidationSeverity.Warning, "a.xhtml", 1, -1, "fixable", "Validation_BareBodyText",
            new BareBodyTextFix("a.xhtml"));
        vm.LoadResults(new[] { Result("b.xhtml", 2, "plain"), fixable });
        System.Collections.Generic.IReadOnlyList<ValidationResult>? requested = null;
        vm.FixRequested += (_, results) => requested = results;
        Window window = new() { Width = 700, Height = 300, Content = new ValidationResultsView { DataContext = vm } };
        window.Show();
        Settle(window);
        Button fix = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FixButton");
        Button skipped = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SkippedRulesButton");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FixAllButton").IsEnabled.Should().BeTrue();
        fix.IsEnabled.Should().BeFalse("nothing is selected");
        skipped.IsEnabled.Should().BeFalse();

        TreeDataGrid grid = window.GetVisualDescendants().OfType<TreeDataGrid>().Single();
        grid.RowSelection!.Select(new IndexPath(1));
        Settle(window);
        fix.IsEnabled.Should().BeTrue();
        fix.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        requested.Should().Equal(fixable);

        vm.SetSkippedRules(SkippedDeadLinks);
        Settle(window);
        skipped.IsEnabled.Should().BeTrue();
        window.Close();
    }
}
