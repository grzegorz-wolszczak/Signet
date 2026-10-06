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
using Signet.Core.Misc;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// The "Spellcheck Editor" dialog's words table (TreeDataGrid): columns, multi-selection feeding the batch commands,
/// navigation on double click. Uses the bundled en_US dictionary ("wrold", "wrongg" are misspelled).
/// </summary>
public sealed class SpellcheckEditorWindowTests
{
    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    private static (SpellcheckEditorWindow Window, SpellcheckEditorViewModel Vm, TreeDataGrid Grid) Show(TempDir temp, string text)
    {
        SettingsStore settings = new(temp.Combine("settings.json"));
        SpellChecker spellChecker = new(settings, temp.Combine("hunspell"), temp.Combine("user"));
        Book book = BookCreator.CreateNewBook("2.0");
        book.GetHtmlResources()[0].SetText(text);
        SpellcheckEditorViewModel vm = new(spellChecker, settings);
        vm.Refresh(book);
        SpellcheckEditorWindow window = new() { DataContext = vm };
        window.Show();
        Settle(window);
        return (window, vm, window.GetVisualDescendants().OfType<TreeDataGrid>().Single());
    }

    private static Point CellCenter(TreeDataGrid grid, Window window, int column, int row)
    {
        Control cell = grid.TryGetCell(column, row)!;
        return cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
    }

    [AvaloniaFact]
    public void The_table_lists_the_misspelled_words_with_their_columns()
    {
        using TempDir temp = new();
        (SpellcheckEditorWindow window, _, TreeDataGrid grid) = Show(temp, "<p>hello wrold wrongg wrold</p>");

        grid.Classes.Should().Contain("gridLines");
        grid.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
            Strings.Get("SpellcheckEditorWindow_Word"), Strings.Get("ReportsWindow_Count"),
            Strings.Get("SpellcheckEditorWindow_Language"), Strings.Get("SpellcheckEditorWindow_Misspelled"));
        grid.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.Value?.ToString())
            .Should().Contain("wrold").And.Contain("wrongg").And.Contain("2").And.NotContain("hello");
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_clicking_selects_several_rows_for_the_batch_commands()
    {
        using TempDir temp = new();
        (SpellcheckEditorWindow window, SpellcheckEditorViewModel vm, TreeDataGrid grid) = Show(temp, "<p>wrold wrongg</p>");

        Point first = CellCenter(grid, window, 0, 0);
        Point second = CellCenter(grid, window, 0, 1);
        window.MouseDown(first, MouseButton.Left);
        window.MouseUp(first, MouseButton.Left);
        Settle(window);
        vm.SingleSelectedRow.Should().NotBeNull("one row is selected");

        window.MouseDown(second, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(second, MouseButton.Left, RawInputModifiers.Control);
        Settle(window);

        grid.RowSelection!.SelectedItems.Should().HaveCount(2);
        vm.SingleSelectedRow.Should().BeNull("two rows are selected");

        vm.IgnoreCommand.Execute(null);

        vm.Words.Should().BeEmpty("both selected words were ignored");
        window.Close();
    }

    [AvaloniaFact]
    public void Double_clicking_a_word_requests_navigation_to_its_first_occurrence()
    {
        using TempDir temp = new();
        (SpellcheckEditorWindow window, SpellcheckEditorViewModel vm, TreeDataGrid grid) = Show(temp, "<p>wrold</p>");
        string? navigatedTo = null;
        vm.NavigationRequested += (path, _) => navigatedTo = path;
        Point point = CellCenter(grid, window, 0, 0);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle(window);

        navigatedTo.Should().Be(vm.Words.Single().BookPath);
        window.Close();
    }
}
