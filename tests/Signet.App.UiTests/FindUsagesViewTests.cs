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
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Find Usages" panel: the tree and the "Group By" menu of the left toolbar.</summary>
public sealed class FindUsagesViewTests
{
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    // The realized, visible rows of the usage list, in display order (recycled containers are hidden).
    private static FindUsagesNode[] RealizedRows(Window window)
    {
        ListBox list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "UsageList");
        return window.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(i => i.IsVisible && list.IndexFromContainer(i) >= 0)
            .OrderBy(list.IndexFromContainer)
            .Select(i => (FindUsagesNode)i.DataContext!)
            .ToArray();
    }

    // Like a click on the row: selects it and focuses its container (the list box itself is not focusable).
    private static void SelectAndFocus(ListBox list, FindUsagesNode node)
    {
        list.SelectedItem = node;
        list.ContainerFromItem(node)!.Focus();
    }

    private static (Window Window, FindUsagesViewModel Vm, ListBox List) ShowPanel(TempDir temp, bool groupByFile, params ClassUsage[] usages)
    {
        FindUsagesViewModel vm = new(new SettingsStore(temp.Combine("settings.json"))) { GroupByFile = groupByFile };
        vm.Load("note", usages);
        Window window = new() { Width = 700, Height = 400, Content = new FindUsagesView { DataContext = vm } };
        window.Show();
        Settle(window);
        return (window, vm, window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "UsageList"));
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
        Window window = new() { Width = 700, Height = 400, Content = new FindUsagesView { DataContext = vm } };
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
        Window window = new() { Width = 700, Height = 400, Content = new FindUsagesView { DataContext = vm } };
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

        ListBox list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "UsageList");
        SelectAndFocus(list, vm.Roots.Single());
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
        (Window window, FindUsagesViewModel vm, ListBox list) = ShowPanel(temp, groupByFile: false, usages);

        vm.Rows.Should().HaveCount(count + 1);
        RealizedRows(window).Should().HaveCountLessThan(100, "a 400 px panel shows a few dozen rows at most")
            .And.HaveElementAt(0, vm.Roots.Single());

        list.ScrollIntoView(vm.Rows[^1]);
        Settle(window);

        RealizedRows(window).Should().HaveCountLessThan(100).And.Contain(vm.Rows[^1]);
        window.Close();
    }

    [AvaloniaFact]
    public void Left_and_Right_collapse_and_expand_the_selected_node_and_move_to_the_parent_and_the_first_child()
    {
        using TempDir temp = new();
        (Window window, FindUsagesViewModel vm, ListBox list) = ShowPanel(
            temp,
            groupByFile: true,
            new ClassUsage("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Text/a.xhtml", 40, 5, 9, ClassUsageKind.ClassAttribute),
            new ClassUsage("OEBPS/Styles/s.css", 0, 1, 2, ClassUsageKind.Selector));
        FindUsagesNode root = vm.Roots.Single();
        FindUsagesNode file = root.Children[0];
        SelectAndFocus(list, file);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Settle(window);

        file.IsExpanded.Should().BeFalse();
        RealizedRows(window).Should().Equal(root, file, root.Children[1], root.Children[1].Children[0]);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Settle(window);

        file.IsExpanded.Should().BeTrue();
        RealizedRows(window).Should().HaveCount(6);

        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        list.SelectedItem.Should().BeSameAs(file.Children[0], "Right on an expanded node goes to its first child");
        list.ContainerFromItem(file.Children[0])!.IsFocused.Should().BeTrue("the keyboard focus follows the selection");

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        list.SelectedItem.Should().BeSameAs(file, "Left on a usage goes to its file");
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_on_a_usage_opens_it()
    {
        using TempDir temp = new();
        ClassUsage usage = new("OEBPS/Text/a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute);
        (Window window, FindUsagesViewModel vm, ListBox list) = ShowPanel(temp, groupByFile: false, usage);
        ClassUsage? activated = null;
        vm.UsageActivated += (_, u) => activated = u;
        SelectAndFocus(list, vm.Rows[1]);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        activated.Should().Be(usage);
        window.Close();
    }
}
