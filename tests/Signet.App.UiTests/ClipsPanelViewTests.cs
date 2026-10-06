using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.MiscEditors;

namespace Signet.App.UiTests;

/// <summary>The docked "Clips" panel: the clips tree (Name, Text), the filter hiding nodes and the selection.</summary>
public sealed class ClipsPanelViewTests
{
    private static void Render(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    // A group and a clip "Clip" with a text, in a temporary store; the panel shown on them.
    private static (Window Window, ClipsViewModel Vm, TreeDataGrid Tree) Show()
    {
        ClipStore store = new(Path.Combine(Path.GetTempPath(), $"signet-clippanel-{System.Guid.NewGuid():N}.json"));
        ClipsViewModel vm = new(store, () => null, () => null, new StatusBarService());
        vm.AddGroupCommand.Execute(null);
        vm.SetSelectedNodes(System.Array.Empty<ClipNodeViewModel>());
        vm.AddEntryCommand.Execute(null);
        vm.Nodes[1].Text = "<p>hello</p>";
        Window window = new() { Width = 600, Height = 400, Content = new ClipsPanelView { DataContext = vm } };
        window.Show();
        Render(window);
        return (window, vm, window.GetVisualDescendants().OfType<TreeDataGrid>().Single());
    }

    private static string[] RowNames(TreeDataGrid tree) =>
        Enumerable.Range(0, tree.Rows!.Count).Select(i => ((ClipNodeViewModel)tree.Rows[i].Model!).Name).ToArray();

    [AvaloniaFact]
    public void The_tree_shows_the_name_and_the_text_of_the_clips()
    {
        (Window window, ClipsViewModel vm, TreeDataGrid tree) = Show();

        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
            Strings.Get("ReportsWindow_Name"), Strings.Get("ReportsWindow_Text"));
        RowNames(tree).Should().Equal(vm.Nodes[0].Name, vm.Nodes[1].Name);
        tree.GetVisualDescendants().OfType<TreeDataGridTextCell>().Select(c => c.Value?.ToString())
            .Should().Contain("<p>hello</p>");
        window.Close();
    }

    [AvaloniaFact]
    public void The_filter_hides_the_nodes_and_keeps_the_selected_one_selected()
    {
        (Window window, ClipsViewModel vm, TreeDataGrid tree) = Show();
        ClipNodeViewModel clip = vm.Nodes[1];
        vm.SetSelectedNodes(new[] { clip });
        Render(window);
        tree.RowSelection!.SelectedItem.Should().BeSameAs(clip);

        vm.FilterText = "no such clip";
        Render(window);
        RowNames(tree).Should().BeEmpty();

        vm.FilterText = clip.Name;
        Render(window);
        RowNames(tree).Should().Contain(clip.Name);
        tree.RowSelection.SelectedItem.Should().BeSameAs(clip, "the view model's selection survives the filter");
        vm.SelectedNode.Should().BeSameAs(clip);
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_click_selects_several_nodes_in_the_view_model()
    {
        (Window window, ClipsViewModel vm, TreeDataGrid tree) = Show();

        tree.RowSelection!.Select(new IndexPath(0));
        tree.RowSelection.Select(new IndexPath(1));
        Render(window);

        vm.SelectedNodes.Should().HaveCount(2);
        window.Close();
    }
}
