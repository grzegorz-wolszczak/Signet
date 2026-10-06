using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Moq;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>The "Saved Searches" panel: the tree (Name, Find, Replace, Controls) edited in place, filter, Load Search.</summary>
public sealed class SearchEditorViewTests
{
    private static void Render(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
        }
    }

    // A group and a search with "cat" → "dog"; the panel shown on them.
    private static (Window Window, SearchEditorViewModel Vm, FindReplaceViewModel FindReplace, TreeDataGrid Tree) Show(TempDir temp)
    {
        FindReplaceViewModel findReplace = new(
            new SettingsStore(temp.Combine("settings.json")), new StatusBarService(), () => null, new Mock<IMultiFileSearchHost>().Object);
        SearchEditorViewModel vm = new(new SavedSearchStore(temp.Combine("searches.json")), findReplace);
        vm.AddGroupCommand.Execute(null);
        vm.SetSelectedNodes(System.Array.Empty<SearchEntryNodeViewModel>());
        vm.AddEntryCommand.Execute(null);
        SearchEntryNodeViewModel search = vm.Nodes.Single(n => !n.IsGroup);
        search.Find = "cat";
        search.Replace = "dog";
        Window window = new() { Width = 900, Height = 400, Content = new SearchEditorView { DataContext = vm } };
        window.Show();
        Render(window);
        return (window, vm, findReplace, window.GetVisualDescendants().OfType<TreeDataGrid>().Single());
    }

    [AvaloniaFact]
    public void The_tree_has_real_columns_edited_in_place_and_a_group_has_only_a_name()
    {
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, TreeDataGrid tree) = Show(temp);

        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
            Strings.Get("ReportsWindow_Name"), Strings.Get("SearchEditorView_ColumnFind"),
            Strings.Get("FindReplaceView_ReplaceButton"), Strings.Get("SearchEditorView_Controls"));
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        SearchEntryNodeViewModel search = vm.Nodes.Single(n => !n.IsGroup);
        TextBox[] groupBoxes = tree.GetVisualDescendants().OfType<TextBox>().Where(t => ReferenceEquals(t.DataContext, group)).ToArray();
        groupBoxes.Count(t => t.IsVisible).Should().Be(1, "a group shows only its name");
        TextBox find = tree.GetVisualDescendants().OfType<TextBox>()
            .Single(t => ReferenceEquals(t.DataContext, search) && t.Text == "cat");

        find.Text = "mouse";

        search.Find.Should().Be("mouse");
        window.Close();
    }

    [AvaloniaFact]
    public void The_filter_hides_rows_and_a_double_click_loads_the_search()
    {
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, FindReplaceViewModel findReplace, TreeDataGrid tree) = Show(temp);
        SearchEntryNodeViewModel search = vm.Nodes.Single(n => !n.IsGroup);

        vm.FilterText = "no such search";
        Render(window);
        tree.Rows!.Count.Should().Be(0);
        vm.FilterText = string.Empty;
        Render(window);
        tree.Rows.Count.Should().Be(2);

        vm.SetSelectedNodes(new[] { search });
        Render(window);
        tree.RowSelection!.SelectedItem.Should().BeSameAs(search);
        Control cell = tree.TryGetCell(0, vm.Nodes.IndexOf(search))!;
        Point point = cell.TranslatePoint(new Point(2, cell.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);

        findReplace.FindText.Should().Be("cat");
        window.Close();
    }
}
