using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Edit Table Of Contents" window.</summary>
public sealed class EditTocWindowTests : IDisposable
{
    private const string DefaultHeadings = "  <h1>Alpha</h1>\n  <h2>Alpha One</h2>\n  <h1>Beta</h1>";

    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    // A book whose TOC is Alpha (h1) > Alpha One (h2), Beta (h1); the window shown on it.
    private (EditTocWindow Window, EditTocViewModel Vm, TreeDataGrid Grid, Book Book) Show(string headings = DefaultHeadings)
    {
        Book book = _disposables.Add(BookCreator.CreateNewBook("3.0"));
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        string text = html.GetText();
        int start = text.IndexOf("<body", StringComparison.Ordinal);
        int end = text.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length;
        html.SetText(text[..start] + "<body>\n" + headings + "\n</body>" + text[end..]);
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
        Control cell = TitleCell(grid, row);
        return cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
    }

    private static TreeDataGridTemplateCell TitleCell(TreeDataGrid grid, int row)
    {
        Control cell = grid.TryGetCell(0, row)!;
        return cell as TreeDataGridTemplateCell
            ?? cell.GetVisualDescendants().OfType<TreeDataGridTemplateCell>().Single();
    }

    private static ToggleButton ExpanderToggle(TreeDataGrid grid, int row) =>
        grid.TryGetCell(0, row)!.GetVisualDescendants().OfType<ToggleButton>().Single();

    private static Point Center(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void DoubleClick(Window window, Control control)
    {
        Point point = Center(control, window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Settle(window);
    }

    [AvaloniaFact]
    public void ExpandCollapseButtons_WhenDisplayed_AreLeftOfTable()
    {
        // Arrange
        (EditTocWindow window, _, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            Button expand = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExpandAllButton");
            Button collapse = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CollapseAllButton");

            // Act
            double tableLeft = grid.TranslatePoint(new Point(0, 0), window)!.Value.X;
            double expandRight = expand.TranslatePoint(new Point(expand.Bounds.Width, expand.Bounds.Height / 2), window)!.Value.X;
            double collapseRight = collapse.TranslatePoint(new Point(collapse.Bounds.Width, collapse.Bounds.Height / 2), window)!.Value.X;

            // Assert
            expandRight.Should().BeLessThan(tableLeft);
            collapseRight.Should().BeLessThan(tableLeft);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DoubleClicking_target_cell_selects_its_entry_and_requests_target_selection()
    {
        // Arrange
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            try
            {
                EditTocNodeViewModel expected = vm.Nodes[1];
                grid.RowSelection!.Select(new IndexPath(0));
                Settle(window);
                vm.SelectedNode.Should().BeSameAs(vm.Nodes[0]);
                int requests = 0;
                vm.SelectTargetRequested += (_, _) => requests++;
                int row = grid.Rows!.ModelIndexToRowIndex(new IndexPath(1));
                Control targetCell = grid.TryGetCell(2, row)!;

                // Act
                DoubleClick(window, targetCell);

                // Assert
                vm.SelectedNode.Should().BeSameAs(expected);
                grid.RowSelection!.SelectedItem.Should().BeSameAs(expected);
                requests.Should().Be(1);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void The_entries_are_a_hierarchical_table_with_resizable_title_level_and_target_columns()
    {
        // Arrange
        (EditTocWindow window, _, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            // Act
            object?[] headers = grid.Columns!.Select(c => c.Header).ToArray();
            int headerCount = grid.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Count();
            bool canResize = grid.CanUserResizeColumns;
            bool canSort = grid.CanUserSortColumns;
            bool hasGridLines = grid.Classes.Contains("gridLines");
            string?[] texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();

            TreeDataGridExpanderCell[] titleCells = grid.GetVisualDescendants()
                .OfType<TreeDataGridExpanderCell>().OrderBy(c => c.RowIndex).ToArray();

            // Assert
            headers.Should().Equal(
                Strings.Get("EditTocWindow_ColumnTitle"), Strings.Get("EditTocWindow_ColumnLevel"), Strings.Get("EditTocWindow_ColumnTarget"));
            headerCount.Should().Be(3, "every column is shown");
            canResize.Should().BeTrue();
            canSort.Should().BeFalse();
            hasGridLines.Should().BeTrue();
            texts.Should().Contain("Alpha").And.Contain("Alpha One").And.Contain("Beta");
            titleCells.Should().HaveCount(3);
            titleCells[0].ShowExpander.Should().BeTrue("Alpha has a child");
            titleCells[1].ShowExpander.Should().BeFalse("Alpha One has no children");
            titleCells[0].Indent.Should().Be(0);
            titleCells[1].Indent.Should().Be(1);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Clicking_the_expander_hides_and_restores_child_entries()
    {
        // Arrange
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            TreeDataGridExpanderCell alphaCell = grid.TryGetCell(0, 0).Should()
                .BeOfType<TreeDataGridExpanderCell>().Subject;

            // Assert
            alphaCell.ShowExpander.Should().BeTrue();
            ToggleButton toggle = ExpanderToggle(grid, 0);
            toggle.IsVisible.Should().BeTrue();

            // Act
            Point collapsePoint = Center(toggle, window);
            window.MouseDown(collapsePoint, MouseButton.Left);
            window.MouseUp(collapsePoint, MouseButton.Left);
            Settle(window);

            // Assert
            alphaCell.IsExpanded.Should().BeFalse();
            vm.Nodes[0].IsExpanded.Should().BeFalse();
            vm.Rows.Should().HaveCount(3, "collapsing does not remove child entries");
            window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().NotContain("Alpha One");

            // Act
            toggle = ExpanderToggle(grid, 0);
            Point expandPoint = Center(toggle, window);
            window.MouseDown(expandPoint, MouseButton.Left);
            window.MouseUp(expandPoint, MouseButton.Left);
            Settle(window);

            // Assert
            vm.Nodes[0].IsExpanded.Should().BeTrue();
            window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("Alpha One");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NextInvalidEntryCommand_WhenIssueIsCollapsed_ExpandsAndSelectsTheEntry()
    {
        // Arrange
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            try
            {
                EditTocNodeViewModel parent = vm.Nodes[0];
                EditTocNodeViewModel invalid = parent.Children[0];
                invalid.Target = string.Empty;
                parent.IsExpanded = false;

                // Act
                vm.AcceptCommand.Execute(null);
                Settle(window);

                // Assert
                Button previous = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Name == "PreviousInvalidEntryButton");
                Button next = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Name == "NextInvalidEntryButton");
                previous.IsVisible.Should().BeTrue();
                next.IsVisible.Should().BeTrue();
                parent.IsExpanded.Should().BeFalse();
                window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)
                    .Should().NotContain("Alpha One");

                // Act
                Point point = Center(next, window);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Settle(window);

                // Assert
                parent.IsExpanded.Should().BeTrue();
                vm.SelectedNode.Should().BeSameAs(invalid);
                grid.RowSelection!.SelectedItem.Should().BeSameAs(invalid);
                window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)
                    .Should().Contain("Alpha One");
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Toolbar_buttons_collapse_and_expand_all_tree_levels()
    {
        // Arrange
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) =
            Show("  <h1>Alpha</h1>\n  <h2>Alpha One</h2>\n  <h3>Alpha One Deep</h3>\n  <h1>Beta</h1>");
        using (book)
        {
            try
            {
                Button expand = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExpandAllButton");
                Button collapse = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CollapseAllButton");

                // Act
                Point collapsePoint = Center(collapse, window);
                window.MouseDown(collapsePoint, MouseButton.Left);
                window.MouseUp(collapsePoint, MouseButton.Left);
                Settle(window);

                // Assert
                vm.Nodes[0].IsExpanded.Should().BeFalse();
                vm.Nodes[0].Children[0].IsExpanded.Should().BeFalse();
                grid.GetVisualDescendants().OfType<TreeDataGridExpanderCell>().Should().HaveCount(2);
                string?[] collapsedTexts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                collapsedTexts.Should().Contain("Alpha").And.Contain("Beta")
                    .And.NotContain("Alpha One").And.NotContain("Alpha One Deep");

                // Act
                Point expandPoint = Center(expand, window);
                window.MouseDown(expandPoint, MouseButton.Left);
                window.MouseUp(expandPoint, MouseButton.Left);
                Settle(window);

                // Assert
                vm.Nodes[0].IsExpanded.Should().BeTrue();
                vm.Nodes[0].Children[0].IsExpanded.Should().BeTrue();
                grid.GetVisualDescendants().OfType<TreeDataGridExpanderCell>().Should().HaveCount(4);
                string?[] expandedTexts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                expandedTexts.Should().Contain("Alpha One").And.Contain("Alpha One Deep");
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Rename_puts_the_selected_title_in_edit_mode_and_the_text_goes_to_the_entry()
    {
        // Arrange
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            // Act
            grid.RowSelection!.SelectedIndex = new IndexPath(1);
            Settle(window);

            // Assert
            vm.SelectedNode.Should().BeSameAs(vm.Rows[2]);

            // Act
            vm.RenameCommand.Execute(null);
            Settle(window);

            TreeDataGridTemplateCell cell = TitleCell(grid, 2);

            // Assert
            cell.IsEditing.Should().BeTrue();
            TextBox editor = cell.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle().Subject;

            // Act
            editor.Text = "Gamma";

            // Assert
            vm.Rows[2].Text.Should().Be("Gamma");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Rename_of_an_entry_hidden_in_a_collapsed_parent_expands_the_parent_and_edits_the_entry()
    {
        // Arrange
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            grid.RowSelection!.SelectedIndex = new IndexPath(0, 0);
            Settle(window);
            vm.CollapseAllCommand.Execute(null);
            Settle(window);

            // Assert
            vm.SelectedNode.Should().BeSameAs(vm.Rows[1], "collapsing keeps the hidden entry selected");
            vm.Rows[0].IsExpanded.Should().BeFalse();

            // Act
            vm.RenameCommand.Execute(null);
            Settle(window);

            // Assert
            vm.Rows[0].IsExpanded.Should().BeTrue();
            TitleCell(grid, 1).IsEditing.Should().BeTrue();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Clicking_a_selected_title_enters_edit_mode()
    {
        // Arrange
        (EditTocWindow window, _, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            grid.RowSelection!.SelectedIndex = new IndexPath(1);
            Settle(window);
            Point beta = TitleCellCenter(grid, window, 2);

            // Act
            window.MouseDown(beta, MouseButton.Left);
            window.MouseUp(beta, MouseButton.Left);
            Settle(window);

            // Assert
            TitleCell(grid, 2).IsEditing.Should().BeTrue();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Moving_an_entry_under_a_leaf_expands_the_new_parent()
    {
        // Arrange
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) =
            Show("  <h1>Alpha</h1>\n  <h1>Beta</h1>\n  <h1>Gamma</h1>");
        using (book)
        {
            grid.RowSelection!.SelectedIndex = new IndexPath(2);
            Settle(window);

            // Act
            vm.MoveRightCommand.Execute(null);
            Settle(window);

            // Assert
            TreeDataGridExpanderCell betaCell = grid.TryGetCell(0, 1).Should()
                .BeOfType<TreeDataGridExpanderCell>().Subject;
            betaCell.ShowExpander.Should().BeTrue();
            betaCell.IsExpanded.Should().BeTrue();
            window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("Gamma");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Ctrl_click_selects_several_entries_and_moving_keeps_the_moved_entry_selected()
    {
        // Arrange
        (EditTocWindow window, EditTocViewModel vm, TreeDataGrid grid, Book book) = Show();
        using (book)
        {
            // Act
            Point alpha = TitleCellCenter(grid, window, 0);
            Point beta = TitleCellCenter(grid, window, 2);
            window.MouseDown(alpha, MouseButton.Left);
            window.MouseUp(alpha, MouseButton.Left);
            window.MouseDown(beta, MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(beta, MouseButton.Left, RawInputModifiers.Control);
            Settle(window);

            // Assert
            grid.RowSelection!.SelectedItems.Should().HaveCount(2);
            vm.SelectedNode.Should().BeNull("two entries are selected");

            // Act
            grid.RowSelection.SelectedIndex = new IndexPath(1);
            Settle(window);
            EditTocNodeViewModel betaNode = vm.Rows[2];
            vm.MoveUpCommand.Execute(null);
            Settle(window);

            // Assert
            vm.Rows[0].Should().BeSameAs(betaNode, "Beta moved above Alpha");
            grid.RowSelection.SelectedItem.Should().BeSameAs(betaNode, "the moved entry stays selected");
            window.Close();
        }
    }
}
