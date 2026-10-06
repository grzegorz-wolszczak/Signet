using System;
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
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Toc;

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Edit Table Of Contents" window.</summary>
public sealed class EditTocWindowTests
{
    // A book whose TOC is Alpha (h1) > Alpha One (h2), Beta (h1); the window shown on it.
    private static (EditTocWindow Window, EditTocViewModel Vm, TreeDataGrid Grid, Book Book) Show()
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        string text = html.GetText();
        int start = text.IndexOf("<body", StringComparison.Ordinal);
        int end = text.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length;
        html.SetText(text[..start] + "<body>\n  <h1>Alpha</h1>\n  <h2>Alpha One</h2>\n  <h1>Beta</h1>\n</body>" + text[end..]);
        new HeadingSelectorModel(book).Apply();
        TocGenerator.GenerateToc(book);

        EditTocViewModel vm = new(book);
        EditTocWindow window = new() { DataContext = vm };
        window.Show();
        Settle(window);
        return (window, vm, window.GetVisualDescendants().OfType<TreeDataGrid>().Single(), book);
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    private static Point TitleCellCenter(TreeDataGrid grid, Window window, int row)
    {
        Control cell = grid.TryGetCell(0, row)!;
        return cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
    }

    [AvaloniaFact]
    public void The_entries_are_a_flat_table_with_resizable_title_level_and_target_columns()
    {
        (EditTocWindow window, _, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            grid.Columns!.Select(c => c.Header).Should().Equal(
                Strings.Get("EditTocWindow_ColumnTitle"), Strings.Get("EditTocWindow_ColumnLevel"), Strings.Get("EditTocWindow_ColumnTarget"));
            grid.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Should().HaveCount(3, "every column is shown");
            grid.CanUserResizeColumns.Should().BeTrue();
            grid.CanUserSortColumns.Should().BeFalse();
            grid.Classes.Should().Contain("gridLines");
            var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            texts.Should().Contain("Alpha").And.Contain("Alpha One").And.Contain("Beta");
            window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Alpha One").Margin.Left
                .Should().BeGreaterThan(window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Alpha").Margin.Left,
                    "the title is indented by its level");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Rename_puts_the_selected_title_in_edit_mode_and_the_text_goes_to_the_entry()
    {
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            grid.RowSelection!.SelectedIndex = new IndexPath(2);
            Settle(window);
            vm.SelectedNode.Should().BeSameAs(vm.Rows[2]);

            vm.RenameCommand.Execute(null);
            Settle(window);

            TreeDataGridCell cell = grid.TryGetCell(0, 2).Should().BeAssignableTo<TreeDataGridCell>().Subject;
            cell.IsEditing.Should().BeTrue();
            TextBox editor = cell.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle().Subject;
            editor.Text = "Gamma";
            vm.Rows[2].Text.Should().Be("Gamma");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Ctrl_click_selects_several_entries_and_moving_keeps_the_moved_entry_selected()
    {
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            Point alpha = TitleCellCenter(grid, window, 0);
            Point beta = TitleCellCenter(grid, window, 2);
            window.MouseDown(alpha, MouseButton.Left);
            window.MouseUp(alpha, MouseButton.Left);
            window.MouseDown(beta, MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(beta, MouseButton.Left, RawInputModifiers.Control);
            Settle(window);

            grid.RowSelection!.SelectedItems.Should().HaveCount(2);
            vm.SelectedNode.Should().BeNull("two entries are selected");

            grid.RowSelection.SelectedIndex = new IndexPath(2);
            Settle(window);
            EditTocNodeViewModel betaNode = vm.Rows[2];
            vm.MoveUpCommand.Execute(null);
            Settle(window);

            vm.Rows[0].Should().BeSameAs(betaNode, "Beta moved above Alpha");
            grid.RowSelection.SelectedItem.Should().BeSameAs(betaNode, "the moved entry stays selected");
            window.Close();
        }
    }
}
