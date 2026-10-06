using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;

namespace Signet.App.UiTests;

/// <summary>The "Custom Epub Layout Designer": the layout tree (Name, Type) and its selection.</summary>
public sealed class EmptyLayoutWindowTests
{
    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    private static (EmptyLayoutWindow Window, EmptyLayoutViewModel Vm, TreeDataGrid Tree) Show()
    {
        EmptyLayoutViewModel vm = new("3.0");
        EmptyLayoutWindow window = new() { DataContext = vm };
        window.Show();
        Settle(window);
        return (window, vm, window.GetVisualDescendants().OfType<TreeDataGrid>().Single());
    }

    // The names of all the rows of the tree (also those scrolled out of view).
    private static string[] RowNames(TreeDataGrid tree) =>
        Enumerable.Range(0, tree.Rows!.Count).Select(i => ((LayoutNode)tree.Rows[i].Model!).Name).ToArray();

    private static string[] Cells(TreeDataGrid tree) =>
        tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.Value?.ToString() ?? string.Empty).ToArray();

    [AvaloniaFact]
    public void The_tree_shows_the_whole_layout_expanded_with_the_type_of_every_item()
    {
        (EmptyLayoutWindow window, EmptyLayoutViewModel vm, TreeDataGrid tree) = Show();

        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
            Strings.Get("ReportsWindow_Name"), Strings.Get("ReportsWindow_Type"));
        tree.Rows!.Count.Should().BeGreaterThan(vm.Root.Children.Count, "the folders are expanded");
        Cells(tree).Should().Contain("EpubRoot").And.Contain("content.opf")
            .And.Contain(Strings.Get("EmptyLayoutWindow_KindFolder"))
            .And.Contain(Strings.Get("EmptyLayoutWindow_KindFile"))
            .And.Contain(Strings.Get("EmptyLayoutWindow_KindMarker"));
        window.Close();
    }

    [AvaloniaFact]
    public void The_selection_follows_the_view_model_both_ways()
    {
        (EmptyLayoutWindow window, EmptyLayoutViewModel vm, TreeDataGrid tree) = Show();
        tree.RowSelection!.SelectedItem.Should().BeSameAs(vm.Root, "the root is selected at first");

        LayoutNode folder = vm.Root.Children.First(c => c.IsFolder);
        tree.RowSelection.SelectedIndex = new IndexPath(0, vm.Root.Children.IndexOf(folder));
        vm.SelectedNode.Should().BeSameAs(folder);

        LayoutNode added = vm.AddFolder("extras")!;
        vm.SelectedNode = added;
        Settle(window);
        tree.RowSelection.SelectedItem.Should().BeSameAs(added);
        RowNames(tree).Should().Contain("extras");

        vm.DeleteSelection();
        Settle(window);
        tree.RowSelection.SelectedItem.Should().BeSameAs(folder, "deleting selects the parent");
        RowNames(tree).Should().NotContain("extras");
        window.Close();
    }
}
