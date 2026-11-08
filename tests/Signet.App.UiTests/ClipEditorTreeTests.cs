using System;
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
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// The Clip Editor tree (a TreeDataGrid): a name is edited only after a double click / F2 (not a single click), and
/// groups are bold with an expand chevron, even an empty group.
/// </summary>
public sealed class ClipEditorTreeTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private (ClipEditorWindow Window, ClipsViewModel ViewModel) ShowEditor()
    {
        ClipStore store = new(_disposables.Add(new TempDir("signet-clipui-")).Combine("file.xml"));
        ClipsViewModel vm = new(store, () => null, () => null, new StatusBarService());
        vm.AddGroupCommand.Execute(null);
        vm.AddEntryCommand.Execute(null);

        // As MainWindowViewModel does: the panel-scoped "Clip Editor → Rename" action (F2 by default), from which
        // the window builds its key bindings.
        SettingsStore settings = new(_disposables.Add(new TempDir("signet-clipui-")).Combine("settings.xml"));
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

    private static string[] MenuHeaders(ClipEditorWindow window) =>
        window.TreeMenu!.Items.OfType<MenuItem>().Skip(1).Select(m => m.Header as string).OfType<string>().ToArray();

    [AvaloniaFact]
    public void The_text_of_every_button_of_the_panel_on_the_right_fits()
    {
        // Arrange
        (ClipEditorWindow window, _) = ShowEditor();

        // Act
        System.Collections.Generic.List<Button> buttons = window.GetVisualDescendants().OfType<Button>()
            .Where(btn => btn.Command is not null && btn.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Stretch).ToList();

        // Assert
        buttons.Should().NotBeEmpty();
        foreach (Button button in buttons)
        {
            Avalonia.Controls.Presenters.ContentPresenter presenter = button.GetVisualDescendants()
                .OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
            presenter.Child!.DesiredSize.Width.Should().BeLessThanOrEqualTo(presenter.Bounds.Width, "the text of '" + button.Content + "' fits the button");
        }

        window.Close();
    }

    [AvaloniaFact]
    public void The_menu_of_an_empty_place_adds_and_the_menu_of_an_element_deletes()
    {
        // Arrange
        (ClipEditorWindow window, ClipsViewModel vm) = ShowEditor();
        TreeDataGrid tree = window.GetVisualDescendants().OfType<TreeDataGrid>().Single();
        ClipNodeViewModel entry = vm.Nodes[1];

        // Act
        tree.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();
        string[] emptyPlace = MenuHeaders(window);
        NameLabel(window, entry).RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();
        string[] element = MenuHeaders(window);

        // Assert
        window.TreeMenu!.Items.OfType<MenuItem>().First().Classes.Should().Contain(Signet.App.Infrastructure.MenuTitle.TitleClass);
        emptyPlace.Should().Equal(
            Strings.Get("ClipEditorWindow_AddEntry"), Strings.Get("ClipEditorWindow_AddGroup"),
            Strings.Get("Common_Import"), Strings.Get("Common_Export"), Strings.Get("ClipEditorWindow_ExportAll"));
        element.Should().Equal(
            Strings.Get("Common_Delete"), Strings.Get("Common_Import"), Strings.Get("Common_Export"), Strings.Get("ClipEditorWindow_ExportAll"));
        vm.SelectedNodes.Should().ContainSingle().Which.Should().BeSameAs(entry);
        window.Close();
    }

    [AvaloniaFact]
    public void Single_click_selects_without_renaming_and_double_click_starts_rename()
    {
        // Act
        (ClipEditorWindow window, ClipsViewModel vm) = ShowEditor();
        ClipNodeViewModel group = vm.Nodes[0];
        Point at = CenterOf(window, NameLabel(window, group));

        Click(window, at);
        Render(window);

        // Assert
        vm.SelectedNode.Should().BeSameAs(group);
        vm.EditingNode.Should().BeNull();
        window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("inPlaceRename"))
            .Should().OnlyContain(t => !t.IsVisible);

        // Act
        Click(window, at);
        Render(window);

        // Assert
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
        // Act
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

        // Assert
        entry.Name.Should().Be(original);
        vm.EditingNode.Should().BeNull();
        window.IsVisible.Should().BeTrue("Esc in the name editor must not close the window");

        // Act
        window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
        Render(window);
        Editor(window).Text = "committed";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Render(window);

        // Assert
        entry.Name.Should().Be("committed");
        vm.EditingNode.Should().BeNull();
        window.Close();
    }

    [AvaloniaFact]
    public void Groups_are_bold_and_have_a_chevron_even_when_empty()
    {
        // Act
        (ClipEditorWindow window, ClipsViewModel vm) = ShowEditor();
        ClipNodeViewModel group = vm.Nodes[0];
        ClipNodeViewModel entry = vm.Nodes[1];

        // Assert
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

        // Act
        Click(window, CenterOf(window, Chevron(group)));
        Render(window);

        // Assert
        vm.EditingNode.Should().BeNull("a click on a chevron never renames");

        // With a clip inside (the nodes are rebuilt after every change), the chevron expands and collapses the group.
        // Act
        vm.SetSelectedNodes(new[] { group });
        vm.AddEntryCommand.Execute(null);
        Render(window);
        ClipNodeViewModel filled = vm.Nodes[0];

        // Assert
        filled.Children.Should().ContainSingle();
        bool expanded = filled.IsExpanded;

        // Act
        Click(window, CenterOf(window, Chevron(filled)));
        Render(window);

        // Assert
        filled.IsExpanded.Should().Be(!expanded);
        vm.EditingNode.Should().BeNull();

        // Act
        Click(window, CenterOf(window, Chevron(filled)));
        Render(window);

        // Assert
        filled.IsExpanded.Should().Be(expanded);
        window.Close();
    }
}
