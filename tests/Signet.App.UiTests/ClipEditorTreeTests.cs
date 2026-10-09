using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Actions;
using Signet.App.Input;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;

namespace Signet.App.UiTests;

/// <summary>
/// The Clip Editor tree (a TreeDataGrid): a name is edited only after a double click / F2 (not a single click), and
/// groups are bold with an expand chevron, even an empty group.
/// </summary>
public sealed class ClipEditorTreeTests
{
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static (ClipEditorWindow Window, ClipsViewModel ViewModel) ShowEditor()
    {
        ClipStore store = new(Path.Combine(Path.GetTempPath(), $"signet-clipui-{System.Guid.NewGuid():N}.json"));
        ClipsViewModel vm = new(store, () => null, () => null, new StatusBarService());
        vm.AddGroupCommand.Execute(null);
        vm.AddEntryCommand.Execute(null);

        // As MainWindowViewModel does: the panel-scoped "Clip Editor → Rename" action (F2 by default), from which
        // the window builds its key bindings.
        SettingsStore settings = new(Path.Combine(Path.GetTempPath(), $"signet-clipui-{System.Guid.NewGuid():N}-settings.json"));
        AppActionRegistry actions = new(new KeyboardShortcutManager(settings), new StatusBarService(), NullLogger<AppActionRegistry>.Instance);
        actions.SetHandler(AppActionIds.ClipEditorRename, () => vm.RenameSelectedCommand.Execute(null));
        vm.ShortcutActions = new[] { actions.Require(AppActionIds.ClipEditorRename) };

        ClipEditorWindow window = new() { DataContext = vm };
        window.Show();
        Render(window);
        return (window, vm);
    }

    private static TextBlock NameLabel(Window window, ClipNodeViewModel node) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.DataContext == node && t.Text == node.Name);

    private static TextBox Editor(Window window) =>
        window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Classes.Contains("inPlaceRename") && t.IsVisible);

    private static Point CenterOf(Window window, Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
    }

    [AvaloniaFact]
    public void Single_click_selects_without_renaming_and_double_click_starts_rename()
    {
        (ClipEditorWindow window, ClipsViewModel vm) = ShowEditor();
        ClipNodeViewModel group = vm.Nodes[0];
        Point at = CenterOf(window, NameLabel(window, group));

        Click(window, at);
        Render(window);

        vm.SelectedNode.Should().BeSameAs(group);
        vm.EditingNode.Should().BeNull();
        window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("inPlaceRename"))
            .Should().OnlyContain(t => !t.IsVisible);

        Click(window, at);
        Render(window);

        vm.EditingNode.Should().BeSameAs(group);
        group.IsExpanded.Should().BeFalse("a double click on a group name must rename it, not expand it");
        TextBox editor = Editor(window);
        editor.IsFocused.Should().BeTrue();
        editor.SelectedText.Should().Be(group.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void F2_starts_rename_Enter_commits_and_Escape_cancels()
    {
        (ClipEditorWindow window, ClipsViewModel vm) = ShowEditor();
        ClipNodeViewModel entry = vm.Nodes[1];
        string original = entry.Name;
        Click(window, CenterOf(window, NameLabel(window, entry)));
        Render(window);

        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        Render(window);
        Editor(window).Text = "cancelled";
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Render(window);

        entry.Name.Should().Be(original);
        vm.EditingNode.Should().BeNull();
        window.IsVisible.Should().BeTrue("Esc in the name editor must not close the window");

        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        Render(window);
        Editor(window).Text = "committed";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Render(window);

        entry.Name.Should().Be("committed");
        vm.EditingNode.Should().BeNull();
        window.Close();
    }

    [AvaloniaFact]
    public void Groups_are_bold_and_have_a_chevron_even_when_empty()
    {
        (ClipEditorWindow window, ClipsViewModel vm) = ShowEditor();
        ClipNodeViewModel group = vm.Nodes[0];
        ClipNodeViewModel entry = vm.Nodes[1];
        group.Children.Should().BeEmpty();

        NameLabel(window, group).FontWeight.Should().Be(FontWeight.Bold);
        NameLabel(window, entry).FontWeight.Should().Be(FontWeight.Normal);

        // The tree's expander cells: every group has a chevron (even an empty one), an entry has none.
        TreeDataGridExpanderCell ExpanderOf(ClipNodeViewModel node) =>
            window.GetVisualDescendants().OfType<TreeDataGridExpanderCell>()
                .Single(c => ReferenceEquals(c.FindAncestorOfType<TreeDataGridRow>()?.Model, node));
        ToggleButton Chevron(ClipNodeViewModel node) =>
            ExpanderOf(node).GetVisualDescendants().OfType<ToggleButton>().Single();
        ExpanderOf(group).ShowExpander.Should().BeTrue("a group has a chevron even when it is empty");
        ExpanderOf(entry).ShowExpander.Should().BeFalse();

        Click(window, CenterOf(window, Chevron(group)));
        Render(window);
        vm.EditingNode.Should().BeNull("a click on a chevron never renames");

        // With a clip inside (the nodes are rebuilt after every change), the chevron expands and collapses the group.
        vm.SetSelectedNodes(new[] { group });
        vm.AddEntryCommand.Execute(null);
        Render(window);
        ClipNodeViewModel filled = vm.Nodes[0];
        filled.Children.Should().ContainSingle();
        bool expanded = filled.IsExpanded;

        Click(window, CenterOf(window, Chevron(filled)));
        Render(window);
        filled.IsExpanded.Should().Be(!expanded);
        vm.EditingNode.Should().BeNull();

        Click(window, CenterOf(window, Chevron(filled)));
        Render(window);
        filled.IsExpanded.Should().Be(expanded);
        window.Close();
    }
}
