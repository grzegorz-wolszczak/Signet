using System;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Menu;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Book checkpoints in the application: the history starts when a book is opened, Edit menu
/// actions carry the state name, reverting keeps the open tabs, and the Checkpoints panel.
/// </summary>
public sealed class CheckpointsTests
{
    private sealed class Session : IDisposable
    {
        public Session(TempDir temp)
        {
            string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
            Vm = new MainWindowViewModel();
            Vm.LoadBook(new ImportEpub(epub).GetBook(), epub);
        }

        public MainWindowViewModel Vm { get; }

        public Book Book => ((IBookWorkspace)Vm).CurrentBook!;

        public CodeTabViewModel OpenFirstHtml()
        {
            Vm.Tabs.CloseAllTabs();
            Vm.Tabs.OpenResources(new Resource[] { Book.GetAllResources().OfType<HtmlResource>().First() });
            return Vm.ActiveCodeTab!;
        }

        public void Dispose() => Vm.CheckpointHistory.Dispose();
    }

    private static string AppendParagraph(CodeTabViewModel tab, string text)
    {
        tab.Document.Text = tab.Document.Text.Replace("</body>", $"<p>{text}</p></body>", StringComparison.Ordinal);
        return text;
    }

    [Fact]
    public void Loading_a_book_starts_the_history_with_a_start_of_session_checkpoint()
    {
        using TempDir temp = new();
        using Session s = new(temp);

        s.Vm.CheckpointHistory.States.Should().HaveCount(2);
        s.Vm.CheckpointHistory.UndoMessage.Should().Be(Strings.Get("Checkpoint_StartOfSession"));

        AppAction before = s.Vm.Actions.Require(AppActionIds.RevertToBefore);
        before.IsEnabled.Should().BeTrue();
        before.Text.Should().Be(Strings.Format("Checkpoint_RevertToBeforeNamed", Strings.Get("Checkpoint_StartOfSession")));
        s.Vm.Actions.Require(AppActionIds.RevertToAfter).IsEnabled.Should().BeFalse();
        s.Vm.Actions.Require(AppActionIds.CreateCheckpoint).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Checkpoint_actions_are_disabled_without_a_book()
    {
        MainWindowViewModel vm = new();

        vm.Actions.Require(AppActionIds.CreateCheckpoint).IsEnabled.Should().BeFalse();
        vm.Actions.Require(AppActionIds.RevertToBefore).IsEnabled.Should().BeFalse();
        vm.Actions.Require(AppActionIds.RevertToAfter).IsEnabled.Should().BeFalse();
        vm.CreateCheckpoint("x").Should().BeFalse();
    }

    [Fact]
    public void Create_checkpoint_action_asks_the_view_for_a_name()
    {
        using TempDir temp = new();
        using Session s = new(temp);
        int requests = 0;
        s.Vm.CreateCheckpointRequested += (_, _) => requests++;

        s.Vm.Actions.Require(AppActionIds.CreateCheckpoint).Execute(null);

        requests.Should().Be(1);
    }

    [Theory]
    [AutoData]
    public void Reverting_restores_the_checkpointed_text_keeps_the_tab_open_and_marks_the_book_modified(string name, string edit)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.OpenFirstHtml();
        string bookPath = tab.Resource.BookPath;
        s.Vm.CreateCheckpoint(name).Should().BeTrue();
        AppendParagraph(tab, edit);

        s.Vm.Actions.Require(AppActionIds.RevertToBefore).Execute(null);

        CodeTabViewModel reverted = s.Vm.ActiveCodeTab!;
        reverted.Resource.BookPath.Should().Be(bookPath);
        reverted.Document.Text.Should().NotContain(edit);
        s.Book.Modified.Should().BeTrue();
        s.Vm.Actions.Require(AppActionIds.RevertToAfter).IsEnabled.Should().BeTrue();

        s.Vm.Actions.Require(AppActionIds.RevertToAfter).Execute(null);

        s.Vm.ActiveCodeTab!.Document.Text.Should().Contain(edit, "the \"after\" state contains the changes made before the revert");
    }

    [Theory]
    [AutoData]
    public void The_checkpoint_name_is_used_in_the_revert_label(string name)
    {
        using TempDir temp = new();
        using Session s = new(temp);

        s.Vm.CreateCheckpoint(name);

        s.Vm.Actions.Require(AppActionIds.RevertToBefore).Text
            .Should().Be(Strings.Format("Checkpoint_RevertToBeforeNamed", name));
    }

    [Theory]
    [AutoData]
    public void Automatic_checkpoint_can_be_rewound_when_the_operation_changed_nothing(string operation)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        int before = s.Vm.CheckpointHistory.States.Count;

        s.Vm.AddCheckpointBefore(operation).Should().BeTrue();
        s.Vm.CheckpointHistory.UndoMessage.Should().Be(Strings.Format("Checkpoint_Before", operation));
        s.Vm.RewindCheckpoint();

        s.Vm.CheckpointHistory.States.Should().HaveCount(before);
    }

    [Theory]
    [AutoData]
    public void Panel_lists_the_states_with_the_current_one_selected_and_reverts_the_selected_one(string name, string edit)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.OpenFirstHtml();
        s.Vm.CreateCheckpoint(name);
        AppendParagraph(tab, edit);
        CheckpointsViewModel panel = s.Vm.CheckpointsPanel;

        panel.Items.Select(i => i.Label).Should().Equal(
            Strings.Get("Checkpoint_StartOfSession"), name, Strings.Get("Checkpoint_CurrentState"));
        panel.SelectedItem.Should().Be(panel.Items[2]);
        panel.RevertCommand.CanExecute(null).Should().BeFalse("the current state cannot be reverted to");

        panel.SelectedItem = panel.Items[1];
        panel.RevertCommand.CanExecute(null).Should().BeTrue();
        panel.RevertCommand.Execute(null);

        s.Vm.ActiveCodeTab!.Document.Text.Should().NotContain(edit);
        panel.Items[1].IsCurrent.Should().BeTrue();
        panel.Items[1].Label.Should().Be(Strings.Format("Checkpoint_CurrentStateWas", name));
        panel.SelectedItem.Should().Be(panel.Items[1]);
    }

    [Fact]
    public void Edit_menu_contains_the_checkpoint_actions_and_view_menu_the_panel_toggle()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        MenuBuilder builder = new(host.Registry, host.Toolbars, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        var menus = builder.Build();

        MenuItemViewModel edit = menus.Single(m => m.Header == "_Edit");
        edit.Items!.Select(i => i.ActionId).Should().ContainInOrder(
            AppActionIds.RevertToBefore, AppActionIds.RevertToAfter, AppActionIds.CreateCheckpoint);

        MenuItemViewModel view = menus.Single(m => m.Header == "_View");
        view.Items!.Select(i => i.ActionId).Should().Contain(AppActionIds.ToggleCheckpoints);
    }

    [Fact]
    public void Deleting_files_in_the_book_browser_creates_a_checkpoint_first()
    {
        using TempDir temp = new();
        using Session s = new(temp);
        OpfModelEntry image = s.Vm.BookBrowser.Nodes.Single(n => n.Header == "Images").Children[0].Entry!;
        int states = s.Vm.CheckpointHistory.States.Count;

        s.Vm.BookBrowser.ApplyDelete(new[] { image });

        s.Vm.CheckpointHistory.States.Should().HaveCount(states + 1);
        s.Vm.CheckpointHistory.UndoMessage.Should().Be(
            Strings.Format("Checkpoint_Before", Strings.Get("CheckpointOp_DeleteFiles")));

        s.Vm.RevertToBeforeCheckpoint();

        s.Book.GetFolderKeeper().GetResourceByBookPathNoThrow(image.Resource.BookPath)
            .Should().NotBeNull("reverting restores the deleted file");
    }

    [Fact]
    public void Split_without_markers_rewinds_its_checkpoint()
    {
        using TempDir temp = new();
        using Session s = new(temp);
        BookBrowserNode text = s.Vm.BookBrowser.Nodes.Single(n => n.Header == "Text").Children[0];
        s.Vm.BookBrowser.UpdateSelection(new object?[] { text });
        int states = s.Vm.CheckpointHistory.States.Count;

        s.Vm.BookBrowser.SplitSelectedCommand.Execute(null);

        s.Vm.CheckpointHistory.States.Should().HaveCount(states);
    }

    [Fact]
    public void Mend_all_html_creates_a_checkpoint_named_after_the_action()
    {
        using TempDir temp = new();
        using Session s = new(temp);

        s.Vm.Actions.Require(AppActionIds.MendHtml).Execute(null);

        s.Vm.CheckpointHistory.UndoMessage.Should().StartWith(Strings.Format("Checkpoint_Before", string.Empty))
            .And.NotContain("&");
    }

    [Theory]
    [AutoData]
    public void Compare_from_the_panel_diffs_the_checkpoint_against_the_unsaved_current_text(string name, string edit)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.OpenFirstHtml();
        string bookPath = tab.Resource.BookPath;
        s.Vm.CreateCheckpoint(name);
        AppendParagraph(tab, edit);
        DiffViewModel? shown = null;
        s.Vm.CompareCheckpointRequested += (_, diff) => shown = diff;
        CheckpointsViewModel panel = s.Vm.CheckpointsPanel;

        panel.SelectedItem = panel.Items.Single(i => i.Label == name);
        panel.CompareCommand.Execute(null);

        shown.Should().NotBeNull();
        shown!.LeftTitle.Should().Be(name);
        shown.Files.Should().ContainSingle().Which.Diff.RightPath.Should().Be(bookPath);
        SideBySideRowItem changed = shown.SideBySideRows[shown.SelectedRowIndex];
        changed.Row.Right.Text.Should().Contain(edit);

        s.Vm.Tabs.CloseAllTabs();
        shown.ActivateRow(shown.SelectedRowIndex);

        s.Vm.ActiveCodeTab!.Resource.BookPath.Should().Be(bookPath);
    }

    [Theory]
    [AutoData]
    public void Creating_a_checkpoint_keeps_unsaved_editor_changes_visible_as_book_changes(string name, string edit)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.OpenFirstHtml();
        s.Book.Modified = false;
        AppendParagraph(tab, edit);

        s.Vm.CreateCheckpoint(name);

        tab.IsModified.Should().BeFalse("the tab text was written to the resource before the checkpoint");
        s.Book.Modified.Should().BeTrue("otherwise closing the application would not prompt to save");
        ((IBookWorkspace)s.Vm).HasUnsavedChanges.Should().BeTrue();
        s.Vm.WindowTitle.Should().Contain("*");
    }

    [Fact]
    public void A_checkpoint_with_an_empty_name_is_shown_as_an_ellipsis_in_the_revert_label()
    {
        using TempDir temp = new();
        using Session s = new(temp);

        s.Vm.CreateCheckpoint(string.Empty);

        s.Vm.Actions.Require(AppActionIds.RevertToBefore).Text
            .Should().Be(Strings.Format("Checkpoint_RevertToBeforeNamed", "…"));
        s.Vm.CheckpointsPanel.Items[1].Label.Should().Be(Strings.Get("Checkpoint_Unnamed"));
    }

    [Theory]
    [AutoData]
    public void Revert_to_before_that_keeps_the_set_of_files_reverts_without_asking(string name, string edit)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        CodeTabViewModel tab = s.OpenFirstHtml();
        s.Vm.CreateCheckpoint(name);
        AppendParagraph(tab, edit);
        CheckpointRevertRequest? asked = null;
        s.Vm.RevertConfirmationRequested += (_, request) => asked = request;

        s.Vm.Actions.Require(AppActionIds.RevertToBefore).Execute(null);

        asked.Should().BeNull();
        s.Vm.CheckpointHistory.CanRedo.Should().BeTrue("the book was reverted right away");
    }

    [Theory]
    [InlineAutoData(false)]
    [InlineAutoData(true)]
    public void Revert_to_before_a_split_lists_the_new_file_first_and_flags_edits_made_in_it(bool editNewFile, string secondPart, string edit)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        HtmlResource created = SplitFirstHtml(s, secondPart);
        if (editNewFile)
        {
            s.Vm.Tabs.OpenResources(new Resource[] { created });
            AppendParagraph(s.Vm.ActiveCodeTab!, edit);
        }

        CheckpointRevertRequest? asked = null;
        s.Vm.RevertConfirmationRequested += (_, request) => asked = request;

        s.Vm.Actions.Require(AppActionIds.RevertToBefore).Execute(null);

        asked.Should().NotBeNull();
        asked!.Forward.Should().BeFalse();
        asked.Rows.Should().Equal(new CheckpointRevertRow(
            Strings.Format(editNewFile ? "CheckpointRevert_RemovedWithEdits" : "CheckpointRevert_Removed", created.BookPath),
            editNewFile));
        s.Book.GetFolderKeeper().GetResourceByBookPathNoThrow(created.BookPath)
            .Should().NotBeNull("nothing is reverted before the user confirms");

        s.Vm.RevertToBeforeCheckpoint();

        s.Book.GetFolderKeeper().GetResourceByBookPathNoThrow(created.BookPath).Should().BeNull();
    }

    [Theory]
    [AutoData]
    public void Revert_to_after_a_split_lists_the_file_that_comes_back(string secondPart)
    {
        using TempDir temp = new();
        using Session s = new(temp);
        string createdPath = SplitFirstHtml(s, secondPart).BookPath;
        s.Vm.RevertToBeforeCheckpoint();
        CheckpointRevertRequest? asked = null;
        s.Vm.RevertConfirmationRequested += (_, request) => asked = request;

        s.Vm.Actions.Require(AppActionIds.RevertToAfter).Execute(null);

        asked.Should().NotBeNull();
        asked!.Forward.Should().BeTrue();
        asked.Rows.Should().Equal(new CheckpointRevertRow(Strings.Format("CheckpointRevert_Restored", createdPath), false));
    }

    // Splits the first HTML file at a split marker from the Book Browser (which creates the "Before: …" checkpoint)
    // and returns the new file.
    private static HtmlResource SplitFirstHtml(Session s, string secondPart)
    {
        CodeTabViewModel tab = s.OpenFirstHtml();
        tab.Document.Text = tab.Document.Text.Replace(
            "</body>", $"{CodeViewModel.SectionMarker}<p>{secondPart}</p></body>", StringComparison.Ordinal);
        BookBrowserNode text = s.Vm.BookBrowser.Nodes.Single(n => n.Header == "Text").Children
            .Single(n => ReferenceEquals(n.Entry?.Resource, tab.Resource));
        s.Vm.BookBrowser.UpdateSelection(new object?[] { text });
        s.Vm.BookBrowser.SplitSelectedCommand.Execute(null);
        return s.Book.GetAllResources().OfType<HtmlResource>()
            .Single(r => r.BookPath.EndsWith("_0001.xhtml", StringComparison.Ordinal));
    }
}
