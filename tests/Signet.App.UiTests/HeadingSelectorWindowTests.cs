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
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The "Generate Table Of Contents" dialog: the headings tree (Title, Level, In TOC) and its selection.</summary>
public sealed class HeadingSelectorWindowTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    private static Point CenterInWindow(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static Button EditTitleButton(HeadingSelectorWindow window) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "EditTitleButton");

    private static Button ExpandAllButton(HeadingSelectorWindow window) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ExpandAllButton");

    private static Button CollapseAllButton(HeadingSelectorWindow window) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CollapseAllButton");

    private static TreeDataGridTemplateCell TitleCell(TreeDataGrid tree, int rowIndex)
    {
        Control cell = tree.TryGetCell(0, rowIndex)!;
        if (cell is TreeDataGridTemplateCell titleCell)
        {
            return titleCell;
        }

        return cell.GetVisualDescendants().OfType<TreeDataGridTemplateCell>().Should().ContainSingle().Subject;
    }

    // Headings Alpha (h1) > Alpha One (h2), Beta (h1); the dialog shown on them.
    private (HeadingSelectorWindow Window, HeadingSelectorViewModel Vm, TreeDataGrid Tree, Book Book) Show(
        string bodyContent = "  <h1>Alpha</h1>\n  <h2>Alpha One</h2>\n  <h1>Beta</h1>")
    {
        Book book = _disposables.Add(BookCreator.CreateNewBook("3.0"));
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        string text = html.GetText();
        int start = text.IndexOf("<body", StringComparison.Ordinal);
        int end = text.IndexOf("</body>", StringComparison.Ordinal) + "</body>".Length;
        html.SetText(text[..start] + "<body>\n" + bodyContent + "\n</body>" + text[end..]);
        HeadingSelectorViewModel vm = new(new HeadingSelectorModel(book));
        HeadingSelectorWindow window = new() { DataContext = vm };
        window.Show();
        Settle(window);
        return (window, vm, window.GetVisualDescendants().OfType<TreeDataGrid>().Single(), book);
    }

    [AvaloniaFact]
    public void The_tree_shows_the_headings_expanded_with_their_level_and_inclusion()
    {
        // Act
        (HeadingSelectorWindow window, HeadingSelectorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            // Assert
            tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
                Strings.Get("EditTocWindow_ColumnTitle"), Strings.Get("EditTocWindow_ColumnLevel"),
                Strings.Get("HeadingSelectorWindow_ColumnInToc"));
            tree.CanUserSortColumns.Should().BeFalse();
            tree.RowsPresenter!.KeepEstimatedScrollExtentStable.Should().BeTrue();
            tree.Rows!.Count.Should().Be(3);
            tree.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text is "Alpha" or "Alpha One" or "Beta")
                .Select(t => t.Text)
                .Should().Equal("Alpha", "Alpha One", "Beta");
            tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.Value?.ToString())
                .Should().Equal("h1", "h2", "h1");

            HeadingNodeViewModel alphaOne = vm.Nodes[0].Children[0];
            CheckBox include = tree.GetVisualDescendants().OfType<CheckBox>().Single(c => ReferenceEquals(c.DataContext, alphaOne));
            bool before = alphaOne.Heading.IncludeInToc;

            // Act
            include.IsChecked = !before;

            // Assert
            alphaOne.Heading.IncludeInToc.Should().Be(!before, "the check box edits the heading");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ExpandCollapseButtons_WhenDisplayed_AreLeftOfTree()
    {
        // Arrange
        (HeadingSelectorWindow window, _, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            try
            {
                Button expand = ExpandAllButton(window);
                Button collapse = CollapseAllButton(window);

                // Act
                double treeLeft = tree.TranslatePoint(new Point(0, 0), window)!.Value.X;
                double expandRight = expand.TranslatePoint(new Point(expand.Bounds.Width, expand.Bounds.Height / 2), window)!.Value.X;
                double collapseRight = collapse.TranslatePoint(new Point(collapse.Bounds.Width, collapse.Bounds.Height / 2), window)!.Value.X;

                // Assert
                expandRight.Should().BeLessThan(treeLeft);
                collapseRight.Should().BeLessThan(treeLeft);
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
        (HeadingSelectorWindow window, HeadingSelectorViewModel vm, TreeDataGrid tree, Book book) =
            Show("  <h1>Alpha</h1>\n  <h2>Alpha One</h2>\n  <h3>Alpha One Deep</h3>\n  <h1>Beta</h1>");
        using (book)
        {
            try
            {
                Button expand = ExpandAllButton(window);
                Button collapse = CollapseAllButton(window);

                // Act
                Click(window, CenterInWindow(collapse, window));
                Settle(window);

                // Assert
                vm.Nodes[0].IsExpanded.Should().BeFalse();
                vm.Nodes[0].Children[0].IsExpanded.Should().BeFalse();
                tree.GetVisualDescendants().OfType<TreeDataGridExpanderCell>().Should().HaveCount(2);
                string?[] collapsedTexts = window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                collapsedTexts.Should().Contain("Alpha").And.Contain("Beta")
                    .And.NotContain("Alpha One").And.NotContain("Alpha One Deep");

                // Act
                Click(window, CenterInWindow(expand, window));
                Settle(window);

                // Assert
                vm.Nodes[0].IsExpanded.Should().BeTrue();
                vm.Nodes[0].Children[0].IsExpanded.Should().BeTrue();
                tree.GetVisualDescendants().OfType<TreeDataGridExpanderCell>().Should().HaveCount(4);
                string?[] expandedTexts = window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                expandedTexts.Should().Contain("Alpha One").And.Contain("Alpha One Deep");
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void ClickTitle_SingleClick_SelectsWithoutEditing()
    {
        // Arrange
        (HeadingSelectorWindow window, HeadingSelectorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            TreeDataGridTemplateCell cell = TitleCell(tree, 0);
            Point point = CenterInWindow(cell, window);

            // Act
            Click(window, point);
            Settle(window);

            // Assert
            tree.RowSelection!.SelectedItem.Should().BeSameAs(vm.Nodes[0]);
            cell.IsEditing.Should().BeFalse();
            cell.GetVisualDescendants().OfType<TextBox>().Should().BeEmpty();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickTitle_DoubleClick_StartsEditing()
    {
        // Arrange
        (HeadingSelectorWindow window, HeadingSelectorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            TreeDataGridTemplateCell cell = TitleCell(tree, 0);
            Point point = CenterInWindow(cell, window);

            // Act
            Click(window, point);
            Settle(window);

            // Assert
            tree.RowSelection!.SelectedItem.Should().BeSameAs(vm.Nodes[0]);
            cell.IsEditing.Should().BeFalse();

            // Act
            Click(window, point);
            Settle(window);

            // Assert
            cell.IsEditing.Should().BeTrue();
            cell.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EditTitleButton_NoSelection_Disabled()
    {
        // Arrange
        (HeadingSelectorWindow window, HeadingSelectorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            Button editTitleButton = EditTitleButton(window);
            tree.RowSelection!.SingleSelect.Should().BeTrue();

            // Act
            vm.SelectedNode = null;
            tree.RowSelection.Clear();
            Settle(window);

            // Assert
            vm.SelectedNode?.TitleText.Should().BeNull();
            vm.EditTitleCommand.CanExecute(null).Should().BeFalse();
            editTitleButton.Command.Should().BeSameAs(vm.EditTitleCommand);
            editTitleButton.IsEffectivelyEnabled.Should().BeFalse();

            // Act
            tree.RowSelection.SelectedIndex = new IndexPath(0);
            Settle(window);

            // Assert
            editTitleButton.IsEffectivelyEnabled.Should().BeTrue();
            vm.SelectedNode.Should().BeSameAs(vm.Nodes[0]);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EditTitleButton_SingleSelection_StartsEditing()
    {
        // Arrange
        (HeadingSelectorWindow window, HeadingSelectorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            Button editTitleButton = EditTitleButton(window);
            tree.RowSelection!.SelectedIndex = new IndexPath(0);
            Settle(window);

            // Act
            editTitleButton.Command!.Execute(null);
            Settle(window);

            // Assert
            TreeDataGridTemplateCell cell = TitleCell(tree, 0);
            cell.IsEditing.Should().BeTrue();
            TextBox editor = cell.GetVisualDescendants().OfType<TextBox>().Should().ContainSingle().Subject;

            // Act
            editor.Text = "Renamed";

            // Assert
            vm.SelectedNode!.TitleText.Should().Be("Renamed");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Changing_the_level_keeps_the_heading_selected_in_the_tree()
    {
        // Act
        (HeadingSelectorWindow window, HeadingSelectorViewModel vm, TreeDataGrid tree, Book book) = Show();
        using (book)
        {
            tree.RowSelection!.SelectedIndex = new IndexPath(1);

            // Assert
            vm.SelectedNode!.TitleText.Should().Be("Beta");

            // Act
            vm.IncreaseLevelCommand.Execute(null);
            Settle(window);

            // Assert
            vm.SelectedNode!.TitleText.Should().Be("Beta");
            vm.SelectedNode.LevelLabel.Should().Be("h2");
            tree.RowSelection.SelectedItem.Should().BeSameAs(vm.SelectedNode, "the rebuilt node is selected again");
            window.Close();
        }
    }
}
