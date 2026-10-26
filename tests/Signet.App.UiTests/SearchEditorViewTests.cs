using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Signet.App.Infrastructure;
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
            new SettingsStore(temp.Combine("settings.xml")), new StatusBarService(), () => null, new Mock<IMultiFileSearchHost>().Object);
        SearchEditorViewModel vm = new(new SavedSearchStore(temp.Combine("searches.xml")), findReplace);
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
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, SearchEditorViewModel vm, _, TreeDataGrid tree) = Show(temp);

        // Assert
        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header).Should().Equal(
            Strings.Get("ReportsWindow_Name"), Strings.Get("SearchEditorView_ColumnFind"),
            Strings.Get("FindReplaceView_ReplaceButton"), Strings.Get("SearchEditorView_Controls"));
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        SearchEntryNodeViewModel search = vm.Nodes.Single(n => !n.IsGroup);
        TextBox[] groupBoxes = tree.GetVisualDescendants().OfType<TextBox>().Where(t => ReferenceEquals(t.DataContext, group)).ToArray();
        groupBoxes.Count(t => t.IsVisible).Should().Be(1, "a group shows only its name");
        TextBox find = tree.GetVisualDescendants().OfType<TextBox>()
            .Single(t => ReferenceEquals(t.DataContext, search) && t.Text == "cat");

        // Act
        find.Text = "mouse";

        // Assert
        search.Find.Should().Be("mouse");
        window.Close();
    }

    [AvaloniaFact]
    public void The_top_toolbar_has_icon_only_buttons_with_tooltips_and_working_commands()
    {
        // Arrange
        using TempDir temp = new();
        // Headless tests skip the desktop startup path, so the icon set is not merged into app resources by
        // default (App.axaml.cs only does it for IClassicDesktopStyleApplicationLifetime).
        new IconThemeManager(new SettingsStore(temp.Combine("icon-theme-settings.xml")), NullLogger<IconThemeManager>.Instance).ApplySaved();

        // Act
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        Button[] toolbar = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("iconToolbar")).ToArray();

        // Assert
        toolbar.Select(b => (string)b.Tag!).Should().Equal(
            "document-save", "add-entry", "add-group", "delete", "fill-controls", "import", "export", "export");
        toolbar.Should().OnlyContain(b => b.Tag is string, "the icon key is the Tag, not Content (Content is scanned for hardcoded user-facing text)");
        toolbar.Select(ToolTip.GetTip).Should().Equal(
            Strings.Get("SearchEditorView_SaveCurrentFR"), Strings.Get("ClipEditorWindow_AddEntry"),
            Strings.Get("ClipEditorWindow_AddGroup"), Strings.Get("Common_Delete"), Strings.Get("SearchEditorView_FillControls"),
            Strings.Get("Common_Import"), Strings.Get("Common_Export"), Strings.Get("ClipEditorWindow_ExportAll"));
        foreach (Button button in toolbar)
        {
            Panel icon = button.GetVisualDescendants().OfType<Panel>().First();
            (icon.GetVisualDescendants().OfType<Image>().Single().Source is not null
                || icon.GetVisualDescendants().OfType<PathIcon>().Single().Data is not null).Should().BeTrue(
                $"the icon key \"{button.Tag}\" resolves to an image or a geometry in the active icon set");
        }

        // Act
        Button addEntry = toolbar.Single(b => (string)b.Tag! == "add-entry");
        int before = vm.Nodes.Count;
        addEntry.Command!.Execute(null);

        // Assert
        vm.Nodes.Count.Should().Be(before + 1, "the toolbar button is wired to the real AddEntryCommand");
        window.Close();
    }

    [AvaloniaFact]
    public void The_left_toolbar_has_icon_only_reload_expand_and_collapse_buttons()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        Button[] panelTools = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("panelTool")).ToArray();

        // Assert
        panelTools.Should().HaveCount(3, "Reload, Expand All, Collapse All");
        panelTools.Select(ToolTip.GetTip).Should().Equal(
            Strings.Get("Common_Reload"), Strings.Get("Common_Expand"), Strings.Get("Common_Collapse"));
        panelTools.Should().OnlyContain(b => b.GetVisualDescendants().OfType<Path>().Any(), "an icon-only button, no text");
        window.Close();
    }

    [AvaloniaFact]
    public void The_filter_hides_rows_and_a_double_click_loads_the_search()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, SearchEditorViewModel vm, FindReplaceViewModel findReplace, TreeDataGrid tree) = Show(temp);
        SearchEntryNodeViewModel search = vm.Nodes.Single(n => !n.IsGroup);

        vm.FilterText = "no such search";
        Render(window);

        // Assert
        tree.Rows!.Count.Should().Be(0);

        // Act
        vm.FilterText = string.Empty;
        Render(window);

        // Assert
        tree.Rows.Count.Should().Be(2);

        // Act
        vm.SetSelectedNodes(new[] { search });
        Render(window);

        // Assert
        tree.RowSelection!.SelectedItem.Should().BeSameAs(search);
        Control cell = tree.TryGetCell(0, vm.Nodes.IndexOf(search))!;
        Point point = cell.TranslatePoint(new Point(2, cell.Bounds.Height / 2), window)!.Value;

        // Act
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Render(window);

        // Assert
        findReplace.FindText.Should().Be("cat");
        window.Close();
    }
}
