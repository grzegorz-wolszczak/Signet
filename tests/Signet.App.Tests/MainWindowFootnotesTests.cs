using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The footnote services of the main window for the Code Editor "Footnotes" submenu (<see cref="ICodeTabHost"/>).</summary>
public sealed class MainWindowFootnotesTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (MainWindowViewModel Sut, Book Book, HtmlResource Chapter) OpenMinimalChapter(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        sut.Tabs.OpenResources(new Resource[] { chapter });
        return (sut, book, chapter);
    }

    // The minimal chapter with a footnote of other origin after "Hello" (marked [7], its note at the end of the body).
    private (MainWindowViewModel Sut, HtmlResource Chapter) OpenChapterWithForeignFootnote(TempDir temp, bool openTab = true)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        string text = chapter.GetText().Replace(
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"",
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"",
            StringComparison.Ordinal);
        text = text.Insert(text.IndexOf("Hello", StringComparison.Ordinal) + 5, "<a epub:type=\"noteref\" href=\"#n7\" id=\"n7-back\">[7]</a>");
        int body = text.LastIndexOf("</body>", StringComparison.Ordinal);
        chapter.SetText(text.Insert(body, "<aside epub:type=\"footnote\" id=\"n7\"><p><a href=\"#n7-back\">7.</a> Other note.</p></aside>\n"));
        sut.LoadBook(book, epub);
        if (openTab)
        {
            sut.Tabs.OpenResources(new Resource[] { chapter });
        }

        return (sut, chapter);
    }

    private static int AfterHello(HtmlResource chapter) => chapter.GetText().IndexOf("Hello", StringComparison.Ordinal) + 5;

    [Fact]
    public async Task A_footnote_is_added_edited_into_the_global_notes_and_removed_with_its_notes_file()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;
            int offset = AfterHello(chapter);

            // Act & Assert
            host.GetFootnoteState(chapter, offset).Should().Be(new FootnoteMenuState(null, FootnoteError.None, false));
            host.GetFootnoteNumbers(chapter, offset).Should().Be((1, 1));

            // Act
            await host.AddFootnote(chapter, offset, "A <i>note</i>.", FootnotePlacement.Local);

            // Assert
            chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\"").And.Contain("A <i>note</i>.");
            sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteAddedLocal"));
            sut.ActiveCodeTab!.DocumentText.Should().Be(chapter.GetText());

            // Act
            FootnoteMenuState onReference = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("[1]", StringComparison.Ordinal))!;

            // Assert
            onReference.Hit!.Side.Should().Be(FootnoteSide.Reference);
            onReference.HasFootnotes.Should().BeTrue();

            // Act
            FootnoteEditState edit = host.GetFootnoteEdit(onReference.Hit)!;

            // Assert
            edit.Content.Should().Be(new FootnoteContent("A <i>note</i>.", false));
            (edit.LocalNumber, edit.GlobalNumber).Should().Be((1, 1));
            edit.ReferenceText[edit.ReferenceStart..edit.ReferenceEnd].Should().StartWith("<a class=\"signet-fn-ref\"").And.EndWith("[1]</a>");

            // Act
            await host.EditFootnote(onReference.Hit, "A <b>new</b> note.", FootnotePlacement.Global);

            HtmlResource notes = FootnoteEditor.FindGlobalFile(book)!;
            notes.Filename.Should().Be(FootnoteEditor.GlobalFileName);

            // Assert
            notes.GetText().Should().Contain("A <b>new</b> note.");
            chapter.GetText().Should().NotContain("A <i>note</i>.").And.NotContain("A <b>new</b> note.");
            sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteMovedGlobal"));
            sut.ActiveCodeTab!.Resource.Should().BeSameAs(chapter);

            // Act
            FootnoteMenuState onNote = host.GetFootnoteState(notes, notes.GetText().IndexOf("A <b>new", StringComparison.Ordinal))!;

            // Assert
            onNote.Hit!.Side.Should().Be(FootnoteSide.Note);

            // Act
            host.GoToFootnote(onNote.Hit);

            // Assert
            sut.ActiveCodeTab!.Resource.Should().BeSameAs(chapter);

            // Act
            await host.RemoveFootnote(onNote.Hit);

            // Assert
            book.GetHtmlResources().Should().NotContain(notes);
            chapter.GetText().Should().NotContain("<a class=\"signet-fn-ref\"").And.Contain("Hello, world.");
            sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteRemoved"));
        });
    }

    [Fact]
    public async Task Editing_only_the_text_keeps_the_footnote_in_place_and_an_invalid_text_changes_nothing()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;

            // Act
            await host.AddFootnote(chapter, AfterHello(chapter), "Old text.", FootnotePlacement.Local);
            FootnoteHit hit = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("Old text.", StringComparison.Ordinal))!.Hit!;

            // Assert
            hit.Side.Should().Be(FootnoteSide.Note);

            // Act & Assert
            host.ValidateFootnoteEdit(hit.Footnote, "a < b").Should().Be(FootnoteError.InvalidContent);

            // Act
            await host.EditFootnote(hit, "a < b", FootnotePlacement.Local);

            // Assert
            chapter.GetText().Should().Contain("Old text.");
            sut.StatusMessage.Should().Be(
                Strings.Format("Status_FootnoteNotEdited", FootnoteViewModel.ErrorText(FootnoteError.InvalidContent)));

            // Arrange
            int scrolledTo = -1;
            sut.ActiveCodeTab!.ScrollToOffsetRequested += offset => scrolledTo = offset;

            // Act
            await host.EditFootnote(hit, "New text.", FootnotePlacement.Local);

            // Assert
            chapter.GetText().Should().Contain("1.</a> New text.</p>").And.NotContain("Old text.");
            FootnoteEditor.FindGlobalFile(book).Should().BeNull();
            sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteEdited"));
            scrolledTo.Should().Be(chapter.GetText().IndexOf("<aside", StringComparison.Ordinal), "the caret goes back to the note");
        });
    }

    [Fact]
    public async Task A_reference_added_to_a_footnote_makes_it_shared_and_removing_it_makes_it_ordinary_again()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;
            await host.AddFootnote(chapter, AfterHello(chapter), "Shared note.", FootnotePlacement.Local);
            int end = chapter.GetText().IndexOf("world", StringComparison.Ordinal) + 5;

            // Act & Assert
            host.GetFootnoteState(chapter, end)!.ReferenceTargetCount.Should().Be(1);

            // Act
            ReferenceTargetList targets = host.GetFootnoteReferenceTargets(chapter, end)!;

            // Assert
            targets.Targets.Should().ContainSingle().Which.Text.Should().Be("Shared note.");

            // Act
            await host.AddFootnoteReference(chapter, end, targets.Targets[targets.Nearest].Footnote);

            // Assert
            chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1a-ref\">[1a]</a>")
                .And.Contain("world<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1b-ref\">[1b]</a>")
                .And.Contain("<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. Shared note.");
            sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteReferenceAdded"));
            sut.ActiveCodeTab!.DocumentText.Should().Be(chapter.GetText());

            // Act
            FootnoteMenuState onSecond = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("[1b]", StringComparison.Ordinal))!;
            onSecond.Hit!.Footnote.IsShared.Should().BeTrue();
            await host.RemoveFootnoteReference(onSecond.Hit);

            // Assert
            chapter.GetText().Should().Contain("Hello<a class=\"signet-fn-ref\" epub:type=\"noteref\" href=\"#fn-1\" id=\"fn-1-ref\">[1]</a>")
                .And.NotContain("[1b]").And.Contain("<a href=\"#fn-1-ref\">1.</a> Shared note.");
            sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteReferenceRemoved"));
        });
    }

    [Fact]
    public async Task Shared_footnotes_in_another_style_open_the_unifying_dialog_and_unifying_rewrites_them()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;

            // Act & Assert
            sut.PrepareFootnoteLetters().Should().BeNull();

            // Assert
            sut.StatusMessage.Should().Be(Strings.Get("Status_NoSharedFootnotes"));

            // Act
            await host.AddFootnote(chapter, AfterHello(chapter), "Shared note.", FootnotePlacement.Local);
            int end = chapter.GetText().IndexOf("world", StringComparison.Ordinal) + 5;
            FootnoteEditor.AddReference(book, chapter, end, FootnoteScanner.Scan(book).Single(), out _,
                FootnoteOptions.Default with { LetterStyle = FootnoteLetterStyle.UpperLatin }).Should().Be(FootnoteError.None);
            sut.ActiveCodeTab!.Reload();

            // Assert
            sut.CountFootnoteLetterMismatches().Should().Be(1, "Preferences say a, b");

            // Act
            FootnoteLettersViewModel letters = sut.PrepareFootnoteLetters()!;

            // Assert
            letters.Style.Should().Be(FootnoteLetterStyle.LowerLatin);
            letters.Notes.Should().ContainSingle();

            // Act
            await letters.UnifyCommand.ExecuteAsync(null);

            // Assert
            chapter.GetText().Should().Contain("[1a]</a>").And.Contain("[1b]</a>").And.NotContain("[1A]");
            sut.StatusMessage.Should().Be(Strings.Format("Status_FootnoteLettersUnified", 1, Strings.Get("FootnoteLetterStyle_LowerLatin")));
            sut.ActiveCodeTab!.DocumentText.Should().Be(chapter.GetText());
            sut.CountFootnoteLetterMismatches().Should().Be(0);
            sut.PrepareFootnoteLetters().Should().BeNull();
            sut.StatusMessage.Should().Be(Strings.Format("Status_FootnoteLettersAlreadyUnified", Strings.Get("FootnoteLetterStyle_LowerLatin")));
        });
    }

    [Fact]
    public async Task A_footnote_reference_that_cannot_be_added_rewinds_the_checkpoint_and_says_why()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — a note to cite, and a position inside a tag, where no reference can go.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;
            await host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);
            Footnote note = FootnoteScanner.Scan(book).Single();
            string before = chapter.GetText();
            int insideTag = before.IndexOf("<p", StringComparison.Ordinal) + 1;
            int states = sut.CheckpointHistory.States.Count;

            // Act
            await host.AddFootnoteReference(chapter, insideTag, note);

            // Assert
            chapter.GetText().Should().Be(before, "nothing was added");
            sut.StatusMessage.Should().Be(
                Strings.Format("Status_FootnoteReferenceNotAdded", FootnoteViewModel.ErrorText(FootnoteError.InsideTag)));
            sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the operation that did nothing is rewound");
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task The_footnote_windows_reach_the_book_through_the_tab_of_the_file()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — the path the Code Editor actually takes: the window hands its result to the tab, the tab to the host.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book book, HtmlResource chapter) = OpenMinimalChapter(temp);
            CodeTabViewModel tab = sut.ActiveCodeTab!;
            tab.UpdateCaret(1, 1, AfterHello(chapter), 'o');

            // Act
            await tab.AddFootnote(new FootnoteRequest("Note.", FootnotePlacement.Local));

            // Assert
            Footnote added = FootnoteScanner.Scan(book).Should().ContainSingle().Subject;
            added.Placement.Should().Be(FootnotePlacement.Local);
            sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteAddedLocal"));

            // Act — a second reference to the same note, added from the "Add Reference to Footnote" window.
            tab.UpdateCaret(1, 1, chapter.GetText().IndexOf("world", StringComparison.Ordinal) + 5, 'd');
            FootnoteReferenceViewModel? references = tab.CreateFootnoteReferenceViewModel();

            // Assert
            references.Should().NotBeNull();
            references!.CanAccept.Should().BeTrue();

            // Act
            await tab.AddFootnoteReference(references.Selected!.Target.Footnote);

            // Assert
            chapter.GetText().Should().Contain("[1a]").And.Contain("[1b]")
                .And.Contain("<a href=\"#fn-1a-ref\">1a</a>, <a href=\"#fn-1b-ref\">1b</a>. Note.");
            FootnoteScanner.Scan(book).Should().OnlyContain(f => f.IsShared && f.NoteId == "fn-1");

            // Act — "Edit Footnote" rewrites the text of the note under the caret.
            tab.UpdateCaret(1, 1, chapter.GetText().IndexOf("[1b]", StringComparison.Ordinal) + 1, '1');
            FootnoteMenuState state = tab.FootnoteStateAtCaret()!;
            state.Hit.Should().NotBeNull();
            await tab.ApplyFootnoteEdit(state.Hit!, new FootnoteRequest("Note, rewritten.", FootnotePlacement.Local));

            // Assert
            chapter.GetText().Should().Contain("Note, rewritten.");
            sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteEdited"));

            // Act — and the same window removes it.
            await tab.ApplyFootnoteEdit(
                tab.FootnoteStateAtCaret()!.Hit!, new FootnoteRequest(string.Empty, FootnotePlacement.Local, Remove: true));

            // Assert
            FootnoteScanner.Scan(book).Should().BeEmpty();
            chapter.GetText().Should().NotContain("signet-fn");
        });
    }

    [Fact]
    public void The_removal_confirmation_is_a_setting()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource _) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;

        // Act & Assert
        host.ConfirmFootnoteRemoval.Should().BeTrue("removing asks by default");

        // Act
        host.ConfirmFootnoteRemoval = false;

        // Assert
        host.ConfirmFootnoteRemoval.Should().BeFalse();
    }

    [Fact]
    public async Task An_invalid_footnote_text_adds_nothing_and_says_why()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;
            string before = chapter.GetText();

            // Act & Assert
            host.ValidateFootnoteContent("a < b").Should().Be(FootnoteError.InvalidContent);

            // Act
            await host.AddFootnote(chapter, AfterHello(chapter), "a < b", FootnotePlacement.Local);

            // Assert
            chapter.GetText().Should().Be(before);
            sut.StatusMessage.Should().Be(
                Strings.Format("Status_FootnoteNotAdded", FootnoteViewModel.ErrorText(FootnoteError.InvalidContent)));
        });
    }

    [Fact]
    public void The_footnote_state_inside_a_tag_has_the_reason_and_no_footnote()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        int insideTag = chapter.GetText().IndexOf("<p>Hello", StringComparison.Ordinal) + 1;

        // Act & Assert
        host.GetFootnoteState(chapter, insideTag).Should().Be(new FootnoteMenuState(null, FootnoteError.InsideTag, false));
    }

    [Fact]
    public async Task Renumbering_numbered_footnotes_reports_that_the_numbering_is_up_to_date()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;

            // Act
            await host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);

            await host.RenumberFootnotes();

            // Assert
            sut.StatusMessage.Should().Be(Strings.Get("Status_FootnotesUpToDate"));
        });
    }

    [Fact]
    public async Task The_placement_of_the_last_added_footnote_is_the_next_default()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;

            // Act
            await host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Global);

            // Assert
            host.LastFootnotePlacement.Should().Be(FootnotePlacement.Global);

            // Act
            await host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);

            // Assert
            host.LastFootnotePlacement.Should().Be(FootnotePlacement.Local);
        });
    }

    [Fact]
    public async Task Tools_renumber_footnotes_renumbers_like_the_context_menu()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithForeignFootnote(temp);

            // Act
            sut.Actions.Require(AppActionIds.RenumberFootnotes).Execute(null);
            await sut.WhenIdleAsync();

            // Assert
            sut.StatusMessage.Should().Be(Strings.Format("Status_FootnotesUpToDateForeignSkipped", 1));
        });
    }

    [Fact]
    public void The_footnote_state_counts_the_footnotes_of_other_origin()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp);
        ICodeTabHost host = sut;

        // Act
        FootnoteMenuState state = host.GetFootnoteState(chapter, chapter.GetText().IndexOf("[7]", StringComparison.Ordinal))!;

        // Assert
        state.Hit!.Footnote.IsSignet.Should().BeFalse();
        state.ForeignCount.Should().Be(1);
    }

    [Fact]
    public async Task Renumbering_leaves_footnotes_of_other_origin_as_they_are_and_warns_about_them()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp);
            ICodeTabHost host = sut;
            int afterWorld = chapter.GetText().IndexOf(", world", StringComparison.Ordinal) + ", world".Length;

            // Act
            await host.AddFootnote(chapter, afterWorld, "Note.", FootnotePlacement.Local);

            await host.RenumberFootnotes();

            // Assert
            chapter.GetText().Should().Contain(">[7]</a>").And.Contain(">7.</a> Other note.")
                .And.Contain("id=\"fn-2-ref\">[2]</a>", "the footnote of other origin before it counts");
            sut.StatusMessage.Should().Be(Strings.Format("Status_FootnotesUpToDateForeignSkipped", 1));
            sut.IsStatusMessageWarning.Should().BeTrue();
        });
    }

    [Fact]
    public async Task The_renumbering_reports_each_stage_as_a_phase_of_five_with_its_own_count()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;
            await host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);
            RecordingProgress progress = new();
            ProgressRunner runner = (_, work, _) => work(progress, CancellationToken.None);
            sut.DelayedProgressRunner = (_, work, _) => work(new RecordingProgress(), CancellationToken.None);

            // Act
            await host.RenumberFootnotes(runner);

            // Assert
            progress.Reports.Should().OnlyContain(r => r.Phase != null && r.Phase.Count == 5, "every report names its phase of five");
            progress.Reports.Select(r => r.Phase!.Index).Distinct().Should().Equal(1, 2, 3, 4, 5);
            progress.Reports.Select(r => r.Phase!.Name).Distinct().Should().Equal(
                Strings.Get("FootnoteRenumber_Phase_Scanning"), Strings.Get("FootnoteRenumber_Phase_Numbering"),
                Strings.Get("FootnoteRenumber_Phase_Writing"), Strings.Get("FootnoteRenumber_Phase_Sorting"),
                Strings.Get("FootnoteRenumber_Phase_Styling"));
            progress.Reports.Where(r => r.Phase!.Index == 2).Should().Contain(
                r => r.Message == Strings.Format("Progress_Detail_Footnote", 0, 1) && r.Fraction == 0, "numbering counts the footnotes (before the one it is on)");
            progress.Reports.Where(r => r.Message == null).Should().OnlyContain(r => r.Fraction == null, "a phase whose count is not known yet has no percent");
        });
    }

    [Fact]
    public async Task Cancelling_the_checkpoint_of_a_quick_edit_leaves_the_book_as_it_was()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;
            string before = chapter.GetText();
            int states = sut.CheckpointHistory.States.Count;
            using CancellationTokenSource pressed = new();
            sut.DelayedProgressRunner = (_, work, cancellable) =>
            {
                cancellable.Should().BeTrue("the window of a checkpoint offers Cancel");
                pressed.Cancel();
                return work(new RecordingProgress(), pressed.Token);
            };

            // Act
            await host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);

            // Assert
            chapter.GetText().Should().Be(before, "the edit never started");
            sut.CheckpointHistory.States.Should().HaveCount(states, "the cancelled checkpoint left nothing behind");
            sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelled", Strings.Get("CheckpointOp_AddFootnote")));
            sut.IsBookOperationBusy.Should().BeFalse();
        });
    }

    [Fact]
    public async Task Cancelling_the_renumbering_brings_the_book_back()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;
            await host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);
            string before = chapter.GetText();
            int states = sut.CheckpointHistory.States.Count;
            using CancellationTokenSource pressed = new();
            ProgressRunner runner = (_, work, cancellable) =>
            {
                cancellable.Should().BeTrue();
                pressed.Cancel();
                return work(new RecordingProgress(), pressed.Token);
            };
            List<(string Title, bool Cancellable)> checkpointRuns = new();
            sut.DelayedProgressRunner = (title, work, cancellable) =>
            {
                checkpointRuns.Add((title, cancellable));
                return work(new RecordingProgress(), CancellationToken.None);
            };
            List<(string Title, bool Cancellable)> restoreRuns = new();
            sut.ProgressRunner = (title, work, cancellable) =>
            {
                restoreRuns.Add((title, cancellable));
                return work(new RecordingProgress(), CancellationToken.None);
            };

            // Act
            await host.RenumberFootnotes(runner);

            // Assert
            Book restored = ((IBookWorkspace)sut).CurrentBook!;
            restored.GetHtmlResources().Single(h => h.Filename == chapter.Filename).GetText().Should().Be(before);
            sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the cancelled renumbering is forgotten");
            sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelledRestored", Strings.Get("FootnoteRenumber_ProgressTitle")));
            checkpointRuns.Select(r => r.Title).Should().Equal(Strings.Format("Checkpoint_Before", Strings.Get("CheckpointOp_RenumberFootnotes")));
            checkpointRuns[0].Cancellable.Should().BeTrue("creating a checkpoint is itself cancellable");
            restoreRuns.Select(r => r.Title).Should().Equal(
                new[] { Strings.Get("Checkpoint_AbandonProgressTitle") },
                "the restore has a window at once (the runner without a delay), so it follows the Cancelling window without a gap");
            restoreRuns[0].Cancellable.Should().BeFalse("restoring the book back is already what Cancel started — there is no further back from it");
        });
    }

    [Fact]
    public async Task Cancelling_the_checkpoint_itself_aborts_the_operation_before_it_ever_starts()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — Cancel is pressed while the checkpoint is still being taken (under DelayedProgressRunner, not
            // the operation's own runner), so the renumbering work below never gets a chance to run at all.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;
            await host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);
            string before = chapter.GetText();
            int states = sut.CheckpointHistory.States.Count;
            using CancellationTokenSource pressed = new();
            string? checkpointTitle = null;
            sut.DelayedProgressRunner = (title, work, cancellable) =>
            {
                checkpointTitle = title;
                cancellable.Should().BeTrue();
                pressed.Cancel();
                return work(new RecordingProgress(), pressed.Token);
            };
            bool mainWorkRan = false;

            // Act
            await host.RenumberFootnotes((_, work, _) =>
            {
                mainWorkRan = true;
                return work(new RecordingProgress(), CancellationToken.None);
            });

            // Assert
            mainWorkRan.Should().BeFalse("the operation's own runner is never reached when the checkpoint itself is cancelled");
            checkpointTitle.Should().Be(Strings.Format("Checkpoint_Before", Strings.Get("CheckpointOp_RenumberFootnotes")));
            chapter.GetText().Should().Be(before);
            sut.CheckpointHistory.States.Should().HaveCount(states, "the cancelled checkpoint was never added");
            sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelled", Strings.Get("FootnoteRenumber_ProgressTitle")));
        });
    }

    [Fact]
    public async Task Starting_a_new_operation_while_a_cancelled_one_is_still_restoring_the_book_is_refused()
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange — reproduces a live crash: renumbering a huge book, cancelling it, and renumbering again right away (before
            // the restore from the checkpoint, which also runs off the UI thread, had finished) threw ObjectDisposedException
            // reading a file of the book that the still-running restore had already disposed.
            using UiCultureScope culture = new("en");
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            ICodeTabHost host = sut;
            await host.AddFootnote(chapter, AfterHello(chapter), "Note.", FootnotePlacement.Local);
            string before = chapter.GetText();
            using CancellationTokenSource pressed = new();
            ProgressRunner runner = (_, work, cancellable) =>
            {
                pressed.Cancel();
                return work(new RecordingProgress(), pressed.Token);
            };

            // Act
            Task first = host.RenumberFootnotes(runner);

            // Assert — while the first operation is running, IsBookOperationBusy is set synchronously and every other
            // action that would itself start a book operation is disabled, not only the one that started this one — the
            // menu/toolbar/shortcut for "Change Footnote Placement" cannot even be clicked.
            sut.IsBookOperationBusy.Should().BeTrue();
            sut.Actions.Require(AppActionIds.ChangeFootnotePlacement).CanExecute(null).Should().BeFalse();

            Task second = host.RenumberFootnotes();

            // Assert — the second call is refused synchronously, before it ever touches the book, instead of racing the first
            // one's cancellation (which puts the book back on a background thread).
            second.IsCompleted.Should().BeTrue("a second book operation must not start while one is already running");
            sut.StatusMessage.Should().Be(Strings.Get("Status_OperationBusy"));
            sut.IsStatusMessageWarning.Should().BeTrue();

            // Act
            await first;

            // Assert — the first (accepted) operation still ran to completion and restored the book from its checkpoint, and
            // the other actions are enabled again.
            Book restored = ((IBookWorkspace)sut).CurrentBook!;
            restored.GetHtmlResources().Single(h => h.Filename == chapter.Filename).GetText().Should().Be(before);
            sut.StatusMessage.Should().Be(Strings.Format("Status_OperationCancelledRestored", Strings.Get("FootnoteRenumber_ProgressTitle")));
            sut.IsBookOperationBusy.Should().BeFalse();
            sut.Actions.Require(AppActionIds.ChangeFootnotePlacement).CanExecute(null).Should().BeTrue();
        });
    }

    [Fact]
    public void The_preview_of_a_footnote_changes_nothing()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
        ICodeTabHost host = sut;
        string before = chapter.GetText();
        int offset = AfterHello(chapter);

        // Act
        FootnoteMarkup preview = host.PreviewFootnote(chapter, offset, offset, "One.\n\nTwo.", FootnotePlacement.Local, 1, null)!;

        // Assert
        preview.Reference.Should().Contain("href=\"#fn-1\"");
        preview.Note.Should().Contain("<p class=\"signet-fn-p\">Two.</p>");
        preview.Page.Should().Contain("<p>Hello" + preview.Reference);
        chapter.GetText().Should().Be(before);

        // Act & Assert
        host.PreviewFootnote(chapter, offset, offset, "a < b", FootnotePlacement.Local, 1, null).Should().BeNull();
    }

    [Fact]
    public void Tools_convert_footnotes_asks_the_view_for_the_dialog()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithForeignFootnote(temp);
        bool raised = false;
        sut.FootnoteConversionRequested += (_, _) => raised = true;

        // Act
        sut.Actions.Require(AppActionIds.ConvertFootnotes).Execute(null);

        // Assert
        raised.Should().BeTrue();
    }

    [Fact]
    public async Task Without_footnotes_of_other_origin_there_is_no_conversion_dialog()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book _, HtmlResource _) = OpenMinimalChapter(temp);

        // Act & Assert
        (await sut.PrepareFootnoteConversionAsync(() => Task.FromResult(true))).Should().BeNull();

        // Assert
        sut.StatusMessage.Should().Be(Strings.Get("Status_NoForeignFootnotes"));
    }

    private sealed class RecordingProgress : IProgress<WorkProgress>
    {
        public List<WorkProgress> Reports { get; } = new();

        public void Report(WorkProgress value)
        {
            lock (Reports)
            {
                Reports.Add(value);
            }
        }
    }

    [Fact]
    public async Task The_plan_reports_a_file_scanned_of_the_books_total()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource _) = OpenChapterWithForeignFootnote(temp);
        RecordingProgress analysisProgress = new();

        // Act
        FootnoteConversionViewModel? dialog = await sut.PrepareFootnoteConversionAsync(() => Task.FromResult(true), analysisProgress: analysisProgress);

        // Assert
        dialog.Should().NotBeNull();
        analysisProgress.Reports.Should().NotBeEmpty();
        analysisProgress.Reports[^1].Fraction.Should().Be(1);
        analysisProgress.Reports.Should().Contain(r => r.Message != null && r.Message.StartsWith("Analyzing the footnotes: file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_conversion_runs_under_the_progress_runner_and_reports_its_progress()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        // No tab is open: its editor document belongs to the thread that made it, and the test continues on another after an await.
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp, openTab: false);
        sut.Tabs.CloseAllTabs();
        RecordingProgress progress = new();
        List<bool> cancellable = new();
        ProgressRunner runner = (_, work, canCancel) =>
        {
            cancellable.Add(canCancel);
            return work(progress, CancellationToken.None);
        };
        FootnoteConversionViewModel dialog = (await sut.PrepareFootnoteConversionAsync(() => Task.FromResult(true), runner))!;
        dialog.SelectAllCommand.Execute(null);

        // Act
        await dialog.ConvertCommand.ExecuteAsync(null);

        // Assert
        // The conversion (a checkpoint was taken, so it can be cancelled) and the analysis after it (it cannot).
        cancellable.Should().Equal(true, false);
        chapter.GetText().Should().Contain("signet-fn-ref");
        dialog.HasProblems.Should().BeFalse();
        progress.Reports.Should().OnlyContain(r => r.Phase != null && r.Phase.Count == 10, "every report names its phase of ten: the stages, the five of the renumbering and the analysis");
        progress.Reports.Select(r => r.Phase!.Index).Should().BeInAscendingOrder().And.Subject.Distinct().Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        progress.Reports.Select(r => r.Phase!.Name).Distinct().Should().Equal(
            Strings.Get("FootnoteConversion_Phase_Preparing"), Strings.Get("FootnoteConversion_Phase_Converting"),
            Strings.Get("FootnoteConversion_Phase_Finishing"), Strings.Get("FootnoteRenumber_Phase_Scanning"),
            Strings.Get("FootnoteRenumber_Phase_Numbering"), Strings.Get("FootnoteRenumber_Phase_Writing"),
            Strings.Get("FootnoteRenumber_Phase_Sorting"), Strings.Get("FootnoteRenumber_Phase_Styling"),
            Strings.Get("FootnoteConversion_Phase_Checking"), Strings.Get("FootnoteConversion_Phase_Analyzing"));
        progress.Reports.Where(r => r.Phase!.Index == 2).Should().Contain(
            r => r.Message == Strings.Format("Progress_Detail_Footnote", 1, 1) && r.Fraction == 1, "converting counts the footnotes");
        progress.Reports.Where(r => r.Phase!.Index is 3 or 9).Should().OnlyContain(
            r => r.Message == null && r.Fraction == null, "the cleaning up and the check cannot tell how far they are");
        progress.Reports.Where(r => r.Phase!.Index == 5).Should().Contain(
            r => r.Message != null && r.Message.StartsWith("footnote", StringComparison.Ordinal), "the renumbering counts the footnotes");
        progress.Reports.Where(r => r.Phase!.Index == 10).Should().Contain(
            r => r.Message != null && r.Message.StartsWith("file", StringComparison.Ordinal), "the analysis counts the files");
        sut.BookBrowser.SuspendRebuilds.Should().BeFalse("the tree follows the book again when the work is done");
    }

    [Fact]
    public async Task Cancelling_the_conversion_brings_the_book_back_and_leaves_the_dialog_as_it_was()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, HtmlResource chapter) = OpenChapterWithForeignFootnote(temp, openTab: false);
        sut.Tabs.CloseAllTabs();
        string before = chapter.GetText();
        int states = sut.CheckpointHistory.States.Count;
        using CancellationTokenSource pressed = new();
        ProgressRunner runner = (_, work, cancellable) =>
        {
            cancellable.Should().BeTrue();
            pressed.Cancel();
            return work(new RecordingProgress(), pressed.Token);
        };
        FootnoteConversionViewModel dialog = (await sut.PrepareFootnoteConversionAsync(() => Task.FromResult(true), runner))!;
        dialog.SelectAllCommand.Execute(null);

        // Act
        await dialog.ConvertCommand.ExecuteAsync(null);

        // Assert
        Book restored = ((IBookWorkspace)sut).CurrentBook!;
        restored.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().Be(before);
        sut.CheckpointHistory.States.Should().HaveCount(states, "the checkpoint of the cancelled conversion is forgotten");
        sut.StatusMessage.Should().Be(Strings.Get("Status_FootnoteConversionCancelled"));
        dialog.HasProblems.Should().BeFalse();
        dialog.Items.Should().HaveCount(1, "the dialog stays as it was");
        dialog.IsConverting.Should().BeFalse();
        sut.BookBrowser.SuspendRebuilds.Should().BeFalse();
    }

    [Theory]
    [AutoData]
    public async Task Abandoning_a_checkpoint_brings_the_book_back_and_forgets_the_failed_state(string operation, string edit)
    {
        await SingleThreadContext.RunAsync(async () =>
        {
            // Arrange
            TempDir temp = _disposables.Add(new TempDir());
            (MainWindowViewModel sut, Book _, HtmlResource chapter) = OpenMinimalChapter(temp);
            string before = chapter.GetText();
            int states = sut.CheckpointHistory.States.Count;

            // Act & Assert
            (await sut.AddCheckpointBeforeAsync(operation)).Should().BeTrue();
            chapter.SetText(before.Replace("Hello", edit, StringComparison.Ordinal));

            // Act
            sut.AbandonCheckpoint().Should().BeTrue();

            Book restored = ((IBookWorkspace)sut).CurrentBook!;

            // Assert
            restored.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml").GetText().Should().Be(before);
            sut.CheckpointHistory.States.Should().HaveCount(states);
            sut.CheckpointHistory.CanRedo.Should().BeFalse();

            // Act & Assert
            sut.AbandonCheckpoint().Should().BeFalse("there is no checkpoint just created any more");
        });
    }
}
