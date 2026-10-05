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

        window.GetVisualDescendants().OfType<TreeViewItem>().Select(i => ((FindUsagesNode)i.DataContext!).Text)
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
        window.GetVisualDescendants().OfType<TreeViewItem>().Select(i => ((FindUsagesNode)i.DataContext!).Text)
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
        string[] VisibleRows() => window.GetVisualDescendants().OfType<TreeViewItem>()
            .Where(i => i.IsEffectivelyVisible).Select(i => ((FindUsagesNode)i.DataContext!).Text).ToArray();
        VisibleRows().Should().HaveCount(5);

        Button collapse = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CollapseAllButton");
        Point center = collapse.TranslatePoint(new Point(collapse.Bounds.Width / 2, collapse.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Settle(window);

        VisibleRows().Should().Equal(Strings.Get("FindUsages_Found"));

        TreeView tree = window.GetVisualDescendants().OfType<TreeView>().Single();
        tree.Focus();
        window.KeyPressQwerty(PhysicalKey.NumPadAdd, RawInputModifiers.Control);
        Settle(window);

        VisibleRows().Should().HaveCount(5, "Ctrl+NumPad + expands everything again");
        window.Close();
    }
}
