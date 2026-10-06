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
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.UiTests;

/// <summary>
/// Rendering of the "Find Usages" panel: the usages tree (a TreeDataGrid with Usage, Line, Col., Kind, Context), its
/// keyboard handling and the "Group By" menu of the left toolbar.
/// </summary>
public sealed class FindUsagesViewTests
{
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static TreeDataGrid Tree(Window window) =>
        window.GetVisualDescendants().OfType<TreeDataGrid>().Single(t => t.Name == "UsageList");

    // The realized, visible rows of the tree, in display order (recycled rows are hidden).
    private static FindUsagesNode[] RealizedRows(Window window) =>
        Tree(window).GetVisualDescendants().OfType<TreeDataGridRow>()
            .Where(r => r.IsVisible && r.RowIndex >= 0)
            .OrderBy(r => r.RowIndex)
            .Select(r => (FindUsagesNode)r.Model!)
            .ToArray();

    // The row index of a node (by its index path from the root).
    private static int RowIndexOf(TreeDataGrid tree, FindUsagesNode node)
    {
        System.Collections.Generic.List<int> path = new();
        for (FindUsagesNode current = node; current.Parent is { } parent; current = parent)
        {
            path.Insert(0, parent.Children.ToList().IndexOf(current));
        }

        path.Insert(0, 0);
        return tree.Rows!.ModelIndexToRowIndex(new IndexPath(path));
    }

    // The first cell of a node's row (in a TreeDataGrid the cells take the keyboard focus).
    private static Control FirstCell(TreeDataGrid tree, FindUsagesNode node) => tree.TryGetCell(0, RowIndexOf(tree, node))!;

    // Like a click on the row: selects it and focuses its first cell.
    private static void SelectAndFocus(TreeDataGrid tree, FindUsagesNode node)
    {
        tree.RowSelection!.SelectedIndex = tree.Rows!.RowIndexToModelIndex(RowIndexOf(tree, node));
        FirstCell(tree, node).Focus();
    }

    private static (Window Window, FindUsagesViewModel Vm, TreeDataGrid Tree) ShowPanel(TempDir temp, bool groupByFile, params ClassUsage[] usages)
    {
        FindUsagesViewModel vm = new(new SettingsStore(temp.Combine("settings.json"))) { GroupByFile = groupByFile };
        vm.Load("note", usages);
        Window window = new() { Width = 900, Height = 400, Content = new FindUsagesView { DataContext = vm } };
        window.Show();
        Settle(window);
        return (window, vm, Tree(window));
    }

    [AvaloniaFact]
    public void The_columns_show_the_line_column_kind_and_context_of_every_usage()
    {
        using TempDir temp = new();
        (Window window, _, TreeDataGrid tree) = ShowPanel(
            temp,
            groupByFile: false,
            new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute, "<p class=\"note\">x</p>"),
            new ClassUsage("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector, ".note { color: red }"));

        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
            Strings.Get("FindUsages_ColumnUsage"), Strings.Get("ValidationResultsView_Line"), Strings.Get("FindUsages_ColumnColumn"),
            Strings.Get("FindUsages_ColumnKind"), Strings.Get("FindUsages_ColumnContext"));
        tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.Value?.ToString())
            .Should().Contain("3").And.Contain("14")
            .And.Contain(Strings.Get("FindUsages_KindAttribute")).And.Contain(Strings.Get("FindUsages_KindSelector"))
            .And.Contain("<p class=\"note\">x</p>").And.Contain(".note { color: red }");
        window.Close();
    }

    // Clicks a column header (sorts by that column; a second click reverses the order).
    private static void ClickHeader(Window window, TreeDataGrid tree, string headerKey)
    {
        TreeDataGridColumnHeader header = tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>()
            .Single(h => Equals(h.Header, Strings.Get(headerKey)));
        Point center = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_Usage_column_sorts_by_file_then_by_line_and_column_as_numbers(bool groupByFile)
    {
        using TempDir temp = new();
        ClassUsage line100 = new("OEBPS/Text/a.xhtml", 900, 100, 3, ClassUsageKind.ClassAttribute);
        ClassUsage line20Col9 = new("OEBPS/Text/a.xhtml", 300, 20, 9, ClassUsageKind.ClassAttribute);
        ClassUsage line20Col2 = new("OEBPS/Text/a.xhtml", 290, 20, 2, ClassUsageKind.ClassAttribute);
        ClassUsage css = new("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector);
        (Window window, _, TreeDataGrid tree) = ShowPanel(temp, groupByFile, line100, line20Col9, css, line20Col2);

        ClickHeader(window, tree, "FindUsages_ColumnUsage");

        ClassUsage?[] ascending = RealizedRows(window).Select(n => n.Usage).Where(u => u is not null).ToArray();
        ascending.Should().Equal(css, line20Col2, line20Col9, line100);
        if (groupByFile)
        {
            RealizedRows(window).Where(n => n.Usage is null).Select(n => n.Text)
                .Should().Equal(Strings.Get("FindUsages_Found"), "OEBPS/Styles/s.css", "OEBPS/Text/a.xhtml");
        }

        ClickHeader(window, tree, "FindUsages_ColumnUsage");

        RealizedRows(window).Select(n => n.Usage).Where(u => u is not null).Should().Equal(ascending.Reverse());
        window.Close();
    }

    [AvaloniaFact]
    public void The_tree_shows_the_usages_and_Group_By_File_switches_to_one_node_per_file()
    {
        using TempDir temp = new();
        SettingsStore settings = new(temp.Combine("settings.json"));
        FindUsagesViewModel vm = new(settings);
        vm.Load("note", new[]
        {
            new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Text/a.xhtml", 40, 5, 9, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector),
        });
        Window window = new() { Width = 900, Height = 400, Content = new FindUsagesView { DataContext = vm } };
        window.Show();
        Settle(window);

        RealizedRows(window).Select(n => n.Text)
            .Should().Equal(Strings.Get("FindUsages_Found"), "OEBPS/Text/a.xhtml:3:14", "OEBPS/Text/a.xhtml:5:9", "OEBPS/Styles/s.css:1:2");

        Button groupBy = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GroupByButton");
        groupBy.Flyout!.ShowAt(groupBy);
        Settle(window);
        MenuItem file = window.GetVisualDescendants().OfType<MenuItem>().SingleOrDefault(m => m.Name == "GroupByFileItem")
            ?? TopLevel.GetTopLevel(groupBy)!.GetVisualDescendants().OfType<MenuItem>().Single(m => m.Name == "GroupByFileItem");
        file.IsChecked.Should().BeFalse();
        Point center = file.TranslatePoint(new Point(file.Bounds.Width / 2, file.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);

        vm.GroupByFile.Should().BeTrue();
        settings.FindUsagesGroupByFile.Should().BeTrue();
        RealizedRows(window).Select(n => n.Text)
            .Should().Equal(
                Strings.Get("FindUsages_Found"),
                "OEBPS/Text/a.xhtml", Strings.Format("FindUsages_LineColumn", 3, 14), Strings.Format("FindUsages_LineColumn", 5, 9),
                "OEBPS/Styles/s.css", Strings.Format("FindUsages_LineColumn", 1, 2));
        window.Close();
    }

    [AvaloniaFact]
    public void Collapse_All_button_and_Ctrl_NumPad_plus_collapse_and_expand_the_tree()
    {
        using TempDir temp = new();
        FindUsagesViewModel vm = new(new SettingsStore(temp.Combine("settings.json"))) { GroupByFile = true };
        vm.Load("note", new[]
        {
            new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector),
        });
        Window window = new() { Width = 900, Height = 400, Content = new FindUsagesView { DataContext = vm } };
        window.Show();
        Settle(window);
        string[] VisibleRows() => RealizedRows(window).Select(n => n.Text).ToArray();
        VisibleRows().Should().HaveCount(5);

        Button collapse = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CollapseAllButton");
        Point center = collapse.TranslatePoint(new Point(collapse.Bounds.Width / 2, collapse.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);

        VisibleRows().Should().Equal(Strings.Get("FindUsages_Found"));

        SelectAndFocus(Tree(window), vm.Roots.Single());
        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.Control);
        Settle(window);

        VisibleRows().Should().HaveCount(5, "Ctrl+NumPad + expands everything again");
        window.Close();
    }
    [AvaloniaFact]
    public void A_huge_result_list_creates_rows_only_for_the_visible_part()
    {
        using TempDir temp = new();
        const int count = 20_000;
        ClassUsage[] usages = Enumerable.Range(0, count)
            .Select(i => new ClassUsage($"OEBPS/Text/ch{i / 400:D3}.xhtml", i * 10, i % 400 + 1, 5, ClassUsageKind.ClassAttribute))
            .ToArray();
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(temp, groupByFile: false, usages);

        tree.Rows!.Count.Should().Be(count + 1);
        RealizedRows(window).Should().HaveCountLessThan(100, "a 400 px panel shows a few dozen rows at most")
            .And.HaveElementAt(0, vm.Roots.Single());

        tree.RowsPresenter!.BringIntoView(count);
        Settle(window);

        RealizedRows(window).Should().HaveCountLessThan(100).And.Contain(vm.Roots.Single().Children[^1]);
        window.Close();
    }

    [AvaloniaFact]
    public void Left_and_Right_collapse_and_expand_the_selected_node_and_move_to_the_parent_and_the_first_child()
    {
        using TempDir temp = new();
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(
            temp,
            groupByFile: true,
            new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Text/a.xhtml", 40, 5, 9, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector));
        FindUsagesNode root = vm.Roots.Single();
        FindUsagesNode file = root.Children[0];
        SelectAndFocus(tree, file);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Settle(window);

        file.IsExpanded.Should().BeFalse();
        RealizedRows(window).Should().Equal(root, file, root.Children[1], root.Children[1].Children[0]);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Settle(window);

        file.IsExpanded.Should().BeTrue();
        RealizedRows(window).Should().HaveCount(6);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        tree.RowSelection!.SelectedItem.Should().BeSameAs(file.Children[0], "Right on an expanded node goes to its first child");
        FirstCell(tree, file.Children[0]).IsFocused.Should().BeTrue("the keyboard focus follows the selection");

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        tree.RowSelection.SelectedItem.Should().BeSameAs(file, "Left on a usage goes to its file");
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_on_a_usage_opens_it()
    {
        using TempDir temp = new();
        ClassUsage usage = new("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute);
        (Window window, FindUsagesViewModel vm, TreeDataGrid tree) = ShowPanel(temp, groupByFile: false, usage);
        ClassUsage? activated = null;
        vm.UsageActivated += (_, u) => activated = u;
        SelectAndFocus(tree, vm.Roots.Single().Children[0]);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        activated.Should().Be(usage);
        window.Close();
    }
}
