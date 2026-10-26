using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using Signet.Core.MiscEditors;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// The "Saved Searches" panel, Calibre-style: a one-column (Name) tree with a read-only preview of the
/// selected entry, and an Add/Edit view (Name + the embedded Find &amp; Replace panel) swapped in over the list.
/// </summary>
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

    // A window with the Saved Searches view, a group ("Group") and one entry ("Entry 1": "cat" -> "dog") in it.
    private static (Window Window, SearchEditorViewModel Vm, FindReplaceViewModel FindReplace, TreeDataGrid Tree) Show(TempDir temp)
    {
        new IconThemeManager(new SettingsStore(temp.Combine("icon-theme-settings.xml")), NullLogger<IconThemeManager>.Instance).ApplySaved();

        var host = new Mock<IMultiFileSearchHost>();
        host.SetupGet(h => h.HasBook).Returns(true);
        SettingsStore settings = new(temp.Combine("settings.xml"));
        FindReplaceViewModel findReplace = new(settings, new StatusBarService(), () => null, host.Object);
        FindReplaceViewModel editFindReplace = new(settings, new StatusBarService(), () => null, host.Object);
        SearchEditorViewModel vm = new(new SavedSearchStore(temp.Combine("searches.xml")), settings, findReplace, editFindReplace);
        vm.AddGroupCommand.Execute(null);
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        group.Name = "Group";

        // A top-level sibling, not nested in "Group" — nothing is selected when it is added, and Add Search
        // places a new entry under the selection's group or, with none, at the root (like here).
        AddViaUi(vm, "Entry 1", "cat", "dog");

        Window window = new() { Width = 900, Height = 500, Content = new SearchEditorView { DataContext = vm } };
        window.Show();
        Render(window);
        return (window, vm, findReplace, window.GetVisualDescendants().OfType<TreeDataGrid>().Single());
    }

    // Drives the real Add Search flow (toolbar button -> fill in the Add/Edit view -> Done) instead of reaching into
    // the model directly, so the tests exercise the same path a user does.
    private static void AddViaUi(SearchEditorViewModel vm, string name, string find, string replace)
    {
        vm.AddEntryCommand.Execute(null);
        vm.EditingName = name;
        vm.FindReplace.FindText = find;
        vm.FindReplace.ReplaceText = replace;
        vm.DoneEditCommand.Execute(null);
    }

    private static SearchEntryNodeViewModel Entry(SearchEditorViewModel vm, string name) =>
        Descendants(vm.Nodes).Single(n => !n.IsGroup && n.Name == name);

    private static System.Collections.Generic.IEnumerable<SearchEntryNodeViewModel> Descendants(
        System.Collections.Generic.IEnumerable<SearchEntryNodeViewModel> nodes)
    {
        foreach (SearchEntryNodeViewModel node in nodes)
        {
            yield return node;
            foreach (SearchEntryNodeViewModel child in Descendants(node.Children))
            {
                yield return child;
            }
        }
    }

    // ================= Tree structure =================

    [AvaloniaFact]
    public void The_tree_has_a_single_Name_column()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, _, _, TreeDataGrid tree) = Show(temp);

        // Assert
        tree.GetVisualDescendants().OfType<TreeDataGridColumnHeader>().Select(h => h.Header)
            .Should().Equal(Strings.Get("ReportsWindow_Name"));
        window.Close();
    }

    private static TextBlock NameLabel(Window window, SearchEntryNodeViewModel node) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.DataContext == node && t.Text == node.Name && t.IsVisible);

    private static TextBox RenameEditor(Window window) =>
        window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("inPlaceRename") && t.IsVisible);

    private static Point CenterOf(Window window, Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
    }

    [AvaloniaFact]
    public void A_group_is_bold_an_entry_is_plain_nothing_starts_out_editable()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");

        // Assert
        NameLabel(window, group).FontWeight.Should().Be(Avalonia.Media.FontWeight.Bold);
        NameLabel(window, entry).FontWeight.Should().Be(Avalonia.Media.FontWeight.Normal);
        window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("inPlaceRename"))
            .Should().OnlyContain(t => !t.IsVisible, "a single click only selects — nothing starts out editable");
        window.Close();
    }

    // Regression: the TreeDataGrid fork's HierarchicalRow.Expand() permanently hides a row's chevron
    // (ShowExpander = false) the first time it is expanded and turns out to have no actual children — so the
    // "has children" hint given to the Expander column must track real children, not just IsGroup, or an empty
    // group's chevron would vanish the moment it is clicked.
    [AvaloniaFact]
    public void A_group_shows_a_chevron_only_once_it_has_children_and_the_chevron_survives_expand_collapse()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, TreeDataGrid tree) = Show(temp);
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");

        TreeDataGridExpanderCell ExpanderOf(SearchEntryNodeViewModel node) =>
            window.GetVisualDescendants().OfType<TreeDataGridExpanderCell>()
                .Single(c => ReferenceEquals(c.FindAncestorOfType<TreeDataGridRow>()?.Model, node));

        // Assert: empty — no chevron to click (clicking it would otherwise hide it for good).
        group.Children.Should().BeEmpty();
        ExpanderOf(group).ShowExpander.Should().BeFalse("empty, there is nothing to expand into yet");
        ExpanderOf(entry).ShowExpander.Should().BeFalse();

        // Act: give the group a child.
        vm.SetSelectedNodes(new[] { group });
        AddViaUi(vm, "Child", "a", "b");
        Render(window);
        group = vm.Nodes.Single(n => n.IsGroup);

        // Assert
        group.Children.Should().ContainSingle();
        ExpanderOf(group).ShowExpander.Should().BeTrue("it now has a child to expand into");

        // Act: click the chevron to expand, then to collapse — it must not disappear either time.
        ToggleButton chevron = ExpanderOf(group).GetVisualDescendants().OfType<ToggleButton>().Single();
        Click(window, CenterOf(window, chevron));
        Render(window);

        // Assert
        group.IsExpanded.Should().BeTrue();
        ExpanderOf(group).ShowExpander.Should().BeTrue("expanding must not hide the chevron");

        // Act
        Click(window, CenterOf(window, ExpanderOf(group).GetVisualDescendants().OfType<ToggleButton>().Single()));
        Render(window);

        // Assert
        group.IsExpanded.Should().BeFalse();
        ExpanderOf(group).ShowExpander.Should().BeTrue("collapsing must not hide the chevron either");
        window.Close();
    }

    [AvaloniaFact]
    public void Single_click_only_selects_a_group_double_click_renames_it_in_place()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        Point at = CenterOf(window, NameLabel(window, group));

        // Act
        Click(window, at);
        Render(window);

        // Assert
        vm.SelectedNode.Should().BeSameAs(group);
        group.IsEditing.Should().BeFalse();

        // Act
        Click(window, at);
        Render(window);

        // Assert
        group.IsEditing.Should().BeTrue();
        group.IsExpanded.Should().BeFalse("a double click on a group name renames it, it does not expand the group");
        TextBox editor = RenameEditor(window);
        editor.IsFocused.Should().BeTrue();
        editor.SelectedText.Should().Be(group.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_commits_an_in_place_group_rename_Escape_discards_it()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        string original = group.Name;
        Point at = CenterOf(window, NameLabel(window, group));
        Click(window, at);
        Click(window, at);
        Render(window);

        // Act
        RenameEditor(window).Text = "cancelled";
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Render(window);

        // Assert
        group.Name.Should().Be(original);
        group.IsEditing.Should().BeFalse();
        window.IsVisible.Should().BeTrue("Escape in the name editor must not close the window");

        // Act
        Click(window, at);
        Click(window, at);
        Render(window);
        RenameEditor(window).Text = "Renamed group";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Render(window);

        // Assert
        group.Name.Should().Be("Renamed group");
        group.IsEditing.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void Losing_focus_commits_an_in_place_group_rename()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        Point at = CenterOf(window, NameLabel(window, group));
        Click(window, at);
        Click(window, at);
        Render(window);
        RenameEditor(window).Text = "Committed by focus loss";

        // Act: focus something else instead of pressing Enter.
        Entry(vm, "Entry 1");
        window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "FilterBox").Focus();
        Render(window);

        // Assert
        group.Name.Should().Be("Committed by focus loss");
        group.IsEditing.Should().BeFalse();
        window.Close();
    }

    // ================= Top toolbar =================

    [AvaloniaFact]
    public void The_top_toolbar_has_icon_only_buttons_with_tooltips_and_working_commands()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        Button[] toolbar = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("iconToolbar")).ToArray();

        // Assert
        toolbar.Select(b => (string)b.Tag!).Should().Equal(
            "document-save", "add-entry", "edit-search", "add-group", "delete", "import", "export", "export-all");
        toolbar.Should().OnlyContain(b => b.Tag is string, "the icon key is the Tag, not Content (Content is scanned for hardcoded user-facing text)");
        toolbar.Select(ToolTip.GetTip).Should().Equal(
            Strings.Get("SearchEditorView_SaveCurrentFR"), Strings.Get("SearchEditorView_AddSearch"),
            Strings.Get("SearchEditorView_EditSearch"), Strings.Get("ClipEditorWindow_AddGroup"), Strings.Get("Common_Delete"),
            Strings.Get("Common_Import"), Strings.Get("Common_Export"), Strings.Get("ClipEditorWindow_ExportAll"));
        foreach (Button button in toolbar)
        {
            Panel icon = button.GetVisualDescendants().OfType<Panel>().First();
            (icon.GetVisualDescendants().OfType<Image>().Single().Source is not null
                || icon.GetVisualDescendants().OfType<PathIcon>().Single().Data is not null).Should().BeTrue(
                $"the icon key \"{button.Tag}\" resolves to an image or a geometry in the active icon set");
        }

        window.Close();
    }

    [AvaloniaFact]
    public void The_left_toolbar_has_icon_only_reload_expand_and_collapse_buttons()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, _, _, _) = Show(temp);
        Button[] panelTools = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("panelTool")).ToArray();

        // Assert
        panelTools.Should().HaveCount(3, "Reload, Expand All, Collapse All");
        panelTools.Select(ToolTip.GetTip).Should().Equal(
            Strings.Get("Common_Reload"), Strings.Get("Common_Expand"), Strings.Get("Common_Collapse"));
        panelTools.Should().OnlyContain(b => b.GetVisualDescendants().OfType<Path>().Any(), "an icon-only button, no text");
        window.Close();
    }

    // ================= Add Search =================

    [AvaloniaFact]
    public void Add_Search_opens_a_blank_Add_Edit_view_and_Done_adds_the_entry()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        int before = vm.Nodes.Count;

        // Act
        vm.AddEntryCommand.Execute(null);

        // Assert
        vm.IsEditing.Should().BeTrue();
        vm.EditingName.Should().BeEmpty();
        vm.FindReplace.FindText.Should().BeEmpty();
        vm.FindReplace.ReplaceText.Should().BeEmpty();

        // Act
        vm.EditingName = "New Search";
        vm.FindReplace.FindText = "foo";
        vm.FindReplace.ReplaceText = "bar";
        vm.FindReplace.RegexDotAll = true;
        vm.DoneEditCommand.Execute(null);

        // Assert
        vm.IsEditing.Should().BeFalse();
        vm.Nodes.Count.Should().Be(before + 1);
        SearchEntryNodeViewModel added = Entry(vm, "New Search");
        added.Find.Should().Be("foo");
        added.Replace.Should().Be("bar");
        SearchControls.Parse(added.Controls).DotAll.Should().BeTrue("the 9 Controls options are edited through the embedded Find & Replace panel");
        vm.SelectedNode.Should().BeSameAs(added);
        window.Close();
    }

    [AvaloniaFact]
    public void Cancel_in_the_Add_Edit_view_discards_the_new_entry()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        int before = vm.Nodes.Count;

        // Act
        vm.AddEntryCommand.Execute(null);
        vm.EditingName = "Discarded";
        vm.CancelEditCommand.Execute(null);

        // Assert
        vm.IsEditing.Should().BeFalse();
        vm.Nodes.Count.Should().Be(before);
        Descendants(vm.Nodes).Should().NotContain(n => n.Name == "Discarded");
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_in_the_Add_Edit_view_discards_it_like_Cancel()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        int before = vm.Nodes.Count;
        vm.AddEntryCommand.Execute(null);
        vm.EditingName = "Discarded";
        Render(window);

        // Act
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Render(window);

        // Assert
        vm.IsEditing.Should().BeFalse();
        vm.Nodes.Count.Should().Be(before);
        Descendants(vm.Nodes).Should().NotContain(n => n.Name == "Discarded");
        window.Close();
    }

    [AvaloniaFact]
    public void A_blank_name_falls_back_to_Unnamed_Search()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);

        // Act
        vm.AddEntryCommand.Execute(null);
        vm.FindReplace.FindText = "x";
        vm.DoneEditCommand.Execute(null);

        // Assert
        Descendants(vm.Nodes).Should().Contain(n => n.Name == Strings.Get("SearchEditor_UnnamedSearch"));
        window.Close();
    }

    // ================= Edit Search =================

    [AvaloniaFact]
    public void Edit_Search_opens_the_Add_Edit_view_prefilled_and_Done_updates_the_entry_in_place()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");
        vm.SetSelectedNodes(new[] { entry });
        int before = vm.Nodes.Count;

        // Act
        vm.EditSearchCommand.Execute(null);

        // Assert
        vm.IsEditing.Should().BeTrue();
        vm.EditingName.Should().Be("Entry 1");
        vm.FindReplace.FindText.Should().Be("cat");
        vm.FindReplace.ReplaceText.Should().Be("dog");

        // Act
        vm.EditingName = "Entry 1 renamed";
        vm.FindReplace.FindText = "mouse";
        vm.DoneEditCommand.Execute(null);

        // Assert
        vm.IsEditing.Should().BeFalse();
        vm.Nodes.Count.Should().Be(before, "editing in place must not add a new entry");
        SearchEntryNodeViewModel updated = Entry(vm, "Entry 1 renamed");
        updated.Find.Should().Be("mouse");
        Descendants(vm.Nodes).Should().NotContain(n => n.Name == "Entry 1");
        window.Close();
    }

    [AvaloniaFact]
    public void Edit_Search_does_nothing_when_a_group_or_nothing_is_selected()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);

        // Act
        vm.SetSelectedNodes(System.Array.Empty<SearchEntryNodeViewModel>());
        vm.EditSearchCommand.Execute(null);

        // Assert
        vm.IsEditing.Should().BeFalse();

        // Act
        vm.SetSelectedNodes(new[] { vm.Nodes.Single(n => n.IsGroup) });
        vm.EditSearchCommand.Execute(null);

        // Assert
        vm.IsEditing.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void Double_clicking_an_entry_opens_Edit_Search_double_clicking_a_group_renames_it_not_Edit_Search()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");
        Point entryPoint = CenterOf(window, NameLabel(window, entry));

        // Act
        Click(window, entryPoint);
        Click(window, entryPoint);
        Render(window);

        // Assert
        vm.IsEditing.Should().BeTrue("double-clicking an entry opens Edit Search, like Calibre");
        vm.CancelEditCommand.Execute(null);

        // Act
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        Point groupPoint = CenterOf(window, NameLabel(window, group));
        Click(window, groupPoint);
        Click(window, groupPoint);
        Render(window);

        // Assert
        vm.IsEditing.Should().BeFalse("a group renames in place, it never opens the Add/Edit view");
        group.IsEditing.Should().BeTrue();
        window.Close();
    }

    // ================= Delete confirmation =================

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Deleting_one_entry_asks_to_confirm_and_Cancel_keeps_it()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");
        vm.SetSelectedNodes(new[] { entry });
        int before = vm.Nodes.Count;

        // Act
        vm.DeleteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        ConfirmWindow confirm = window.OwnedWindows.OfType<ConfirmWindow>().Single();
        Button okButton = confirm.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton");

        // Assert
        okButton.Classes.Should().Contain(ButtonClasses.Danger, "Delete is destructive");

        // Act
        confirm.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.Nodes.Count.Should().Be(before, "Cancel keeps everything");
        window.Close();
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Deleting_one_entry_and_confirming_removes_it()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");
        vm.SetSelectedNodes(new[] { entry });
        int before = vm.Nodes.Count;

        // Act
        vm.DeleteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        ConfirmWindow confirm = window.OwnedWindows.OfType<ConfirmWindow>().Single();
        confirm.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        await System.Threading.Tasks.Task.Yield();
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.Nodes.Count.Should().Be(before - 1);
        Descendants(vm.Nodes).Should().NotContain(n => n.Name == "Entry 1");
        window.Close();
    }

    [AvaloniaFact]
    public void Deleting_a_group_and_an_entry_reports_both_counts()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchDeleteCounts? seen = null;
        vm.DeleteConfirmRequested += (_, e) => seen = e;
        vm.SetSelectedNodes(new SearchEntryNodeViewModel[] { vm.Nodes.Single(n => n.IsGroup), Entry(vm, "Entry 1") });

        // Act
        vm.DeleteCommand.Execute(null);

        // Assert
        seen.Should().Be(new SearchDeleteCounts(1, 1));
        window.Close();
    }

    [AvaloniaFact]
    public void Delete_with_nothing_selected_does_not_ask_to_confirm()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        bool raised = false;
        vm.DeleteConfirmRequested += (_, _) => raised = true;
        vm.SetSelectedNodes(System.Array.Empty<SearchEntryNodeViewModel>());

        // Act
        vm.DeleteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Assert
        raised.Should().BeFalse();
        window.OwnedWindows.OfType<ConfirmWindow>().Should().BeEmpty();
        window.Close();
    }

    // ================= Details preview =================

    [AvaloniaFact]
    public void Selecting_an_entry_shows_a_read_only_preview_selecting_a_group_or_several_hides_it()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);

        // Act
        vm.SetSelectedNodes(new[] { entry });

        // Assert
        vm.ShowDetails.Should().BeTrue();

        // Act
        vm.SetSelectedNodes(new[] { group });

        // Assert
        vm.ShowDetails.Should().BeFalse("a group has no Find/Replace/Controls to preview");

        // Act
        vm.SetSelectedNodes(new[] { entry, group });

        // Assert
        vm.ShowDetails.Should().BeFalse("several selected, like Calibre, shows nothing");

        // Act
        vm.SetSelectedNodes(System.Array.Empty<SearchEntryNodeViewModel>());

        // Assert
        vm.ShowDetails.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void The_tree_and_the_details_preview_are_split_by_a_draggable_row_splitter()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, _, _, _) = Show(temp);
        GridSplitter splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single(s => s.Name == "DetailsSplitter");

        // Assert
        splitter.ResizeDirection.Should().Be(GridResizeDirection.Rows);
        window.Close();
    }

    [AvaloniaFact]
    public void The_details_preview_fields_are_read_only_and_match_the_selected_entry()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");

        // Act
        vm.SetSelectedNodes(new[] { entry });
        Render(window);
        TextBox[] previewBoxes = window.GetVisualDescendants().OfType<TextBox>()
            .Where(t => !ReferenceEquals(t.DataContext, entry) && t.IsReadOnly && t.Text == entry.Find).ToArray();

        // Assert
        previewBoxes.Should().NotBeEmpty("the Find preview field shows the selected entry's Find text");
        previewBoxes.Should().OnlyContain(t => t.IsReadOnly);
        window.Close();
    }

    // ================= Filter =================

    [AvaloniaFact]
    public void The_filter_hides_rows_and_the_clear_button_resets_it()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, TreeDataGrid tree) = Show(temp);

        // Act
        vm.FilterText = "no such search";
        Render(window);

        // Assert
        tree.Rows!.Count.Should().Be(0);

        // Act
        vm.ClearFilterCommand.Execute(null);
        Render(window);

        // Assert
        vm.FilterText.Should().BeEmpty();
        tree.Rows.Count.Should().Be(2, "the group and its entry are both back");
        window.Close();
    }

    [AvaloniaFact]
    public void The_filter_clear_button_only_shows_once_there_is_text_to_clear()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        Button clear = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FilterClearButton");

        // Assert
        clear.IsVisible.Should().BeFalse();

        // Act
        vm.FilterText = "cat";
        Render(window);

        // Assert
        clear.IsVisible.Should().BeTrue();
        window.Close();
    }

    // ================= Fill Controls (context menu) =================

    [AvaloniaFact]
    public void Fill_Controls_is_offered_from_the_trees_context_menu_not_the_toolbar()
    {
        // Arrange
        using TempDir temp = new();

        // Act
        (Window window, SearchEditorViewModel vm, _, TreeDataGrid tree) = Show(temp);

        // Assert
        window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("iconToolbar"))
            .Should().NotContain(b => (string)b.Tag! == "fill-controls");
        tree.ContextMenu!.Open(tree);
        Dispatcher.UIThread.RunJobs();
        MenuItem item = tree.ContextMenu!.Items.OfType<MenuItem>().Single();
        item.Header.Should().Be(Strings.Get("SearchEditorView_FillControls"));
        item.Command.Should().BeSameAs(vm.FillControlsCommand);
        window.Close();
    }

    [AvaloniaFact]
    public void Fill_Controls_copies_the_options_from_the_first_selected_entry_to_the_others()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        AddViaUi(vm, "Entry 2", "x", "y");
        SearchEntryNodeViewModel first = Entry(vm, "Entry 1");
        SearchEntryNodeViewModel second = Entry(vm, "Entry 2");
        first.Controls = "RX WR";
        second.Controls = "NL";
        vm.SetSelectedNodes(new[] { first, second });

        // Act
        vm.FillControlsCommand.Execute(null);

        // Assert
        Entry(vm, "Entry 2").Controls.Should().Be("RX WR");
        window.Close();
    }

    // ================= Import / Export (unchanged plumbing, still reachable from the toolbar) =================

    [AvaloniaFact]
    public void Import_and_export_still_raise_their_events_from_the_toolbar()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        bool importRaised = false;
        bool? exportAll = null;
        vm.ImportRequested += (_, _) => importRaised = true;
        vm.ExportRequested += (_, all) => exportAll = all;

        // Act
        vm.ImportCommand.Execute(null);
        vm.ExportAllCommand.Execute(null);

        // Assert
        importRaised.Should().BeTrue();
        exportAll.Should().Be(true);
        window.Close();
    }

    // ================= Regression: deleting a nested subgroup =================

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Deleting_a_subgroup_nested_inside_a_group_does_not_throw()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, TreeDataGrid tree) = Show(temp);
        SearchEntryNodeViewModel outer = vm.Nodes.Single(n => n.IsGroup);
        vm.SetSelectedNodes(new[] { outer });
        vm.AddGroupCommand.Execute(null);
        Render(window);
        outer = vm.Nodes.Single(n => n.IsGroup);
        SearchEntryNodeViewModel subGroup = outer.Children.Single(n => n.IsGroup);
        outer.IsExpanded = true;
        Render(window);
        vm.SetSelectedNodes(new[] { subGroup });
        Render(window);

        // Act
        vm.DeleteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        ConfirmWindow confirm = window.OwnedWindows.OfType<ConfirmWindow>().Single();
        confirm.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        await System.Threading.Tasks.Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Render(window);

        // Assert
        vm.Nodes.Single(n => n.IsGroup).Children.Should().NotContain(n => n.IsGroup);
        window.Close();
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Deleting_an_expanded_subgroup_with_an_entry_inside_it_does_not_throw()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, TreeDataGrid tree) = Show(temp);
        SearchEntryNodeViewModel outer = vm.Nodes.Single(n => n.IsGroup);
        vm.SetSelectedNodes(new[] { outer });
        vm.AddGroupCommand.Execute(null);
        Render(window);
        outer = vm.Nodes.Single(n => n.IsGroup);
        outer.IsExpanded = true;
        SearchEntryNodeViewModel subGroup = outer.Children.Single(n => n.IsGroup);
        vm.SetSelectedNodes(new[] { subGroup });
        Render(window);

        // A real entry nested two levels deep, under the expanded subgroup.
        AddViaUi(vm, "Nested entry", "p", "q");
        outer = vm.Nodes.Single(n => n.IsGroup);
        outer.IsExpanded = true;
        subGroup = outer.Children.Single(n => n.IsGroup);
        subGroup.IsExpanded = true;
        Render(window);
        subGroup.Children.Should().ContainSingle(n => n.Name == "Nested entry");
        vm.SetSelectedNodes(new[] { subGroup });
        Render(window);

        // Act
        vm.DeleteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        ConfirmWindow confirm = window.OwnedWindows.OfType<ConfirmWindow>().Single();
        confirm.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OkButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        await System.Threading.Tasks.Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Render(window);

        // Assert
        Descendants(vm.Nodes).Should().NotContain(n => n.Name == "Nested entry" || n.IsGroup && n != vm.Nodes.Single(g => g.IsGroup));
        window.Close();
    }

    // ================= Selection: click again to deselect =================

    [AvaloniaFact]
    public void Clicking_the_only_selected_row_again_deselects_it()
    {
        // Arrange: select programmatically — two real clicks far enough apart not to count as a double click
        // cannot be simulated reliably in a headless test, but a single click reaching the (already selected) row
        // is exactly the gesture this covers.
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");
        vm.SetSelectedNodes(new[] { entry });
        Render(window);
        Point at = CenterOf(window, NameLabel(window, entry));

        // Act
        Click(window, at);
        Render(window);

        // Assert
        vm.SelectedNodes.Should().BeEmpty();
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_different_row_selects_it_instead_of_deselecting()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");
        Click(window, CenterOf(window, NameLabel(window, group)));
        Render(window);
        vm.SelectedNodes.Should().Equal(group);

        // Act
        Click(window, CenterOf(window, NameLabel(window, entry)));
        Render(window);

        // Assert
        vm.SelectedNodes.Should().Equal(entry);
        window.Close();
    }

    [AvaloniaFact]
    public void Double_clicking_a_selected_entry_still_opens_Edit_Search_instead_of_deselecting()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");
        Point at = CenterOf(window, NameLabel(window, entry));
        Click(window, at);
        Render(window);

        // Act: click it again, fast enough to count as a double click.
        Click(window, at);
        Render(window);

        // Assert
        vm.IsEditing.Should().BeTrue("the second click of a double click opens Edit Search, it must not be treated as a deselect");
        vm.SelectedNodes.Should().Equal(entry);
        window.Close();
    }

    // The batch actions on the right (Load Search, Find Next, Replace, Replace/Find, Replace All, Count All) only
    // make sense once a book is loaded and the selection resolves to at least one entry.
    [AvaloniaFact]
    public void The_batch_search_actions_are_disabled_until_an_entry_or_a_group_is_selected()
    {
        // Arrange: Show() leaves the entry it just added selected, so clear that first.
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        vm.SetSelectedNodes(System.Array.Empty<SearchEntryNodeViewModel>());

        // Assert: nothing selected yet.
        vm.FindCommand.CanExecute(null).Should().BeFalse();
        vm.ReplaceCurrentCommand.CanExecute(null).Should().BeFalse();
        vm.ReplaceCommand.CanExecute(null).Should().BeFalse();
        vm.ReplaceAllCommand.CanExecute(null).Should().BeFalse();
        vm.CountAllCommand.CanExecute(null).Should().BeFalse();

        // Act
        vm.SetSelectedNodes(new[] { Entry(vm, "Entry 1") });

        // Assert
        vm.FindCommand.CanExecute(null).Should().BeTrue();
        vm.ReplaceCurrentCommand.CanExecute(null).Should().BeTrue();
        vm.ReplaceCommand.CanExecute(null).Should().BeTrue();
        vm.ReplaceAllCommand.CanExecute(null).Should().BeTrue();
        vm.CountAllCommand.CanExecute(null).Should().BeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public void The_batch_search_actions_are_disabled_when_no_book_is_loaded_even_with_a_selection()
    {
        // Arrange
        using TempDir temp = new();
        var host = new Mock<IMultiFileSearchHost>();
        host.SetupGet(h => h.HasBook).Returns(false);
        SettingsStore settings = new(temp.Combine("settings.xml"));
        FindReplaceViewModel findReplace = new(settings, new StatusBarService(), () => null, host.Object);
        FindReplaceViewModel editFindReplace = new(settings, new StatusBarService(), () => null, host.Object);
        SearchEditorViewModel vm = new(new SavedSearchStore(temp.Combine("searches.xml")), settings, findReplace, editFindReplace);
        AddViaUi(vm, "Entry 1", "cat", "dog");
        vm.SetSelectedNodes(new[] { Entry(vm, "Entry 1") });

        // Act & Assert: selected, but no book — still disabled.
        vm.LoadSearchCommand.CanExecute(null).Should().BeFalse();
        vm.FindCommand.CanExecute(null).Should().BeFalse();
        vm.ReplaceCurrentCommand.CanExecute(null).Should().BeFalse();
        vm.ReplaceCommand.CanExecute(null).Should().BeFalse();
        vm.ReplaceAllCommand.CanExecute(null).Should().BeFalse();
        vm.CountAllCommand.CanExecute(null).Should().BeFalse();

        // Act: a book becomes available (MainWindowViewModel calls this when one opens).
        host.SetupGet(h => h.HasBook).Returns(true);
        vm.RefreshBookAvailability();

        // Assert
        vm.FindCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void The_batch_search_actions_are_enabled_for_a_selected_group_too()
    {
        // Arrange
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);

        // Act: "Group" is empty (Entry 1 is a root sibling, not inside it) in the fixture, so give it a child first.
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        vm.SetSelectedNodes(new[] { group });
        AddViaUi(vm, "In group", "x", "y");

        // Assert
        vm.FindCommand.CanExecute(null).Should().BeTrue("a group selection counts as all the entries under it");
        window.Close();
    }

    [AvaloniaFact]
    public void Load_Search_is_enabled_only_for_a_single_selected_entry()
    {
        // Arrange: Show() leaves the entry it just added selected, so clear that first.
        using TempDir temp = new();
        (Window window, SearchEditorViewModel vm, _, _) = Show(temp);
        SearchEntryNodeViewModel entry = Entry(vm, "Entry 1");
        SearchEntryNodeViewModel group = vm.Nodes.Single(n => n.IsGroup);
        vm.SetSelectedNodes(System.Array.Empty<SearchEntryNodeViewModel>());

        // Assert: nothing selected.
        vm.LoadSearchCommand.CanExecute(null).Should().BeFalse();

        // Act & Assert: a single entry.
        vm.SetSelectedNodes(new[] { entry });
        vm.LoadSearchCommand.CanExecute(null).Should().BeTrue();

        // Act & Assert: a group, not an entry.
        vm.SetSelectedNodes(new[] { group });
        vm.LoadSearchCommand.CanExecute(null).Should().BeFalse("Load Search loads a single search's Find/Replace/options, a group has none of its own");

        // Act & Assert: more than one selected.
        vm.SetSelectedNodes(new[] { entry, group });
        vm.LoadSearchCommand.CanExecute(null).Should().BeFalse();
        window.Close();
    }
}
