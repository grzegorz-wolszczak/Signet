using System;
using System.IO;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.MiscEditors;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for the clips panel/editor (<see cref="ClipsViewModel"/>): CRUD, persistence,
/// pasting with and without a selection, CSS AutoFill.
/// </summary>
public sealed class ClipsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"signet-clips-{Guid.NewGuid():N}");

    public ClipsViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort
        }
    }

    private ClipStore Store() => new(Path.Combine(_dir, "clips.json"));

    private static CodeTabViewModel NewCodeTab(Book book)
    {
        HtmlResource html = book.GetAllResources().OfType<HtmlResource>().First();
        var model = new TabManagerModel();
        OpenTab tab = model.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        return new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);
    }

    [Fact]
    public void AddPendingEntry_adds_an_unnamed_clip_at_the_top_and_selects_it()
    {
        using UiCultureScope culture = new("en");
        var clips = new ClipsViewModel(Store(), () => null, () => null, new StatusBarService());
        clips.AddGroupCommand.Execute(null);

        ClipNodeViewModel? added = clips.AddPendingEntry("<em>x</em>");

        added.Should().NotBeNull();
        clips.Nodes.First().Should().BeSameAs(added);
        (added!.Name, added.Text, added.IsGroup).Should().Be(("Unnamed Entry", "<em>x</em>", false));
        clips.SelectedNode.Should().BeSameAs(added);
        clips.LibraryRoot.Children[0].Text.Should().Be("<em>x</em>");
    }

    [Fact]
    public void AddEntry_and_Save_persist_across_reload()
    {
        ClipStore store = Store();
        var statusBar = new StatusBarService();
        var clips = new ClipsViewModel(store, () => null, () => null, statusBar);

        clips.AddGroupCommand.Execute(null);
        ClipNodeViewModel group = clips.Nodes.Single();
        group.Name = "Styles";
        clips.SetSelectedNodes(new[] { group });

        clips.AddEntryCommand.Execute(null);
        // RebuildNodes() replaced the tree with new instances, so take a fresh reference.
        ClipNodeViewModel entry = clips.Nodes.Single().Children.Single();
        entry.Name = "Highlight";
        entry.Text = "<em>\\1</em>";

        clips.SaveCommand.Execute(null);

        var reloaded = new ClipsViewModel(store, () => null, () => null, statusBar);
        ClipNodeViewModel reloadedEntry = reloaded.Nodes.Single().Children.Single();
        reloadedEntry.Name.Should().Be("Highlight");
        reloadedEntry.Text.Should().Be("<em>\\1</em>");
    }

    [Fact]
    public void PasteText_without_an_active_tab_reports_a_status_message_and_returns_false()
    {
        var statusBar = new StatusBarService();
        var clips = new ClipsViewModel(Store(), () => null, () => null, statusBar);

        bool applied = clips.PasteText("<em>x</em>");

        applied.Should().BeFalse();
        statusBar.CurrentMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void PasteText_with_a_selection_wraps_it_using_the_backslash_one_placeholder()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();
        CodeTabViewModel tab = NewCodeTab(book);
        int start = tab.DocumentText.IndexOf("Chapter 1", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        tab.UpdateSelection(start, start + "Chapter 1".Length);

        var clips = new ClipsViewModel(Store(), () => tab, () => book, new StatusBarService());

        bool applied = clips.PasteText("<em>\\1</em>");

        applied.Should().BeTrue();
        tab.DocumentText.Should().Contain("<em>Chapter 1</em>");
    }

    [Fact]
    public void AutoFillCommand_adds_a_group_of_clips_derived_from_the_book_css_classes()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();

        var clips = new ClipsViewModel(Store(), () => null, () => book, new StatusBarService());
        int before = clips.Nodes.Count;

        clips.AutoFillCommand.Execute(null);

        clips.Nodes.Count.Should().BeGreaterThan(before);
    }

    [Fact]
    public void GetLeafClips_excludes_groups()
    {
        var clips = new ClipsViewModel(Store(), () => null, () => null, new StatusBarService());
        clips.AddGroupCommand.Execute(null);
        clips.SetSelectedNodes(new[] { clips.Nodes.Single() });
        clips.AddEntryCommand.Execute(null);

        var leaves = clips.GetLeafClips();

        leaves.Should().ContainSingle();
    }
    [Fact]
    public void BeginRename_enters_edit_mode_without_changing_the_name()
    {
        var clips = new ClipsViewModel(Store(), () => null, () => null, new StatusBarService());
        clips.AddGroupCommand.Execute(null);
        ClipNodeViewModel group = clips.Nodes.Single();
        string original = group.Name;

        clips.BeginRename(group);

        clips.EditingNode.Should().BeSameAs(group);
        (group.IsEditing, group.EditText, group.Name).Should().Be((true, original, original));
    }

    [Fact]
    public void CommitRename_applies_the_typed_name_and_leaves_edit_mode()
    {
        var clips = new ClipsViewModel(Store(), () => null, () => null, new StatusBarService());
        clips.AddGroupCommand.Execute(null);
        ClipNodeViewModel group = clips.Nodes.Single();
        string newName = new Fixture().Create<string>();

        clips.BeginRename(group);
        group.EditText = newName;
        clips.CommitRename();

        group.Name.Should().Be(newName);
        group.IsEditing.Should().BeFalse();
        clips.EditingNode.Should().BeNull();
        clips.IsDataModified.Should().BeTrue();
    }

    [Fact]
    public void CancelRename_discards_the_typed_name()
    {
        var clips = new ClipsViewModel(Store(), () => null, () => null, new StatusBarService());
        clips.AddGroupCommand.Execute(null);
        ClipNodeViewModel group = clips.Nodes.Single();
        string original = group.Name;

        clips.BeginRename(group);
        group.EditText = new Fixture().Create<string>();
        clips.CancelRename();

        group.Name.Should().Be(original);
        group.IsEditing.Should().BeFalse();
        clips.EditingNode.Should().BeNull();
    }

    [Fact]
    public void BeginRename_on_another_node_commits_the_previous_edit()
    {
        var clips = new ClipsViewModel(Store(), () => null, () => null, new StatusBarService());
        clips.AddGroupCommand.Execute(null);
        clips.AddEntryCommand.Execute(null);
        ClipNodeViewModel first = clips.Nodes[0];
        ClipNodeViewModel second = clips.Nodes[1];
        string newName = new Fixture().Create<string>();

        clips.BeginRename(first);
        first.EditText = newName;
        clips.BeginRename(second);

        first.Name.Should().Be(newName);
        first.IsEditing.Should().BeFalse();
        clips.EditingNode.Should().BeSameAs(second);
    }
    [Fact]
    public void AddEntry_after_deleting_the_selected_group_adds_the_clip_to_the_visible_tree()
    {
        var clips = new ClipsViewModel(Store(), () => null, () => null, new StatusBarService());
        clips.AddGroupCommand.Execute(null);
        clips.SetSelectedNodes(new[] { clips.Nodes.Single() });
        clips.DeleteCommand.Execute(null);

        clips.AddEntryCommand.Execute(null);

        clips.SelectedNode.Should().BeNull();
        clips.Nodes.Should().ContainSingle().Which.IsGroup.Should().BeFalse();
        clips.LibraryRoot.Children.Should().ContainSingle();
    }
    [Fact]
    public void ModifiedMarker_is_an_asterisk_only_while_there_are_unsaved_changes()
    {
        var clips = new ClipsViewModel(Store(), () => null, () => null, new StatusBarService());
        clips.ModifiedMarker.Should().BeEmpty();

        clips.AddEntryCommand.Execute(null);
        clips.ModifiedMarker.Should().Be("*");

        clips.SaveCommand.Execute(null);
        clips.ModifiedMarker.Should().BeEmpty();
    }

    [Fact]
    public void DiscardChanges_restores_the_library_saved_on_disk()
    {
        ClipStore store = Store();
        var clips = new ClipsViewModel(store, () => null, () => null, new StatusBarService());
        clips.AddGroupCommand.Execute(null);
        clips.SaveCommand.Execute(null);
        clips.AddEntryCommand.Execute(null);

        clips.DiscardChanges();

        clips.Nodes.Should().ContainSingle().Which.IsGroup.Should().BeTrue();
        clips.IsDataModified.Should().BeFalse();
        clips.ModifiedMarker.Should().BeEmpty();
    }
}
