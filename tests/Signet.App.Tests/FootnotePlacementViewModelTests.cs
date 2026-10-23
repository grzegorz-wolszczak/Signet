using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of the "Change Footnote Placement" dialog's view model (<see cref="FootnotePlacementViewModel"/>).</summary>
public sealed class FootnotePlacementViewModelTests
{
    private const string Body = "<p>First sentence.</p>\n<p>Second sentence.</p>\n<p>Third sentence.</p>";

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static int After(HtmlResource html, string text) =>
        html.GetText().IndexOf(text, StringComparison.Ordinal) + text.Length;

    // A chapter with two local footnotes ("local one", "local two") and one global ("global one").
    private static (Book Book, HtmlResource Html) NewBook()
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(Body));
        FootnoteEditor.Add(book, html, After(html, "First"), "local one", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, html, After(html, "Second"), "local two", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, html, After(html, "Third"), "global one", FootnotePlacement.Global).Should().Be(FootnoteError.None);
        return (book, html);
    }

    private static FootnotePlacementViewModel NewViewModel(
        Book book, Func<IReadOnlyList<PlacementItem>, HtmlResource?, Task<PlacementOutcome?>>? apply = null) =>
        new(FootnoteEditor.PlanPlacement(book), book.GetHtmlResourcesExcludingNav(), FootnoteEditor.FindGlobalFile(book),
            (_, _) => { }, apply ?? ((_, _) => Task.FromResult<PlacementOutcome?>(new PlacementOutcome(0, 0))));

    [Fact]
    public void The_list_has_every_footnote_of_Signet_with_its_kind_and_text_and_nothing_is_checked()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource _) = NewBook();
        using (book)
        {
            // Act
            FootnotePlacementViewModel sut = NewViewModel(book);

            // Assert
            sut.Rows.Select(r => (r.Text, r.Kind, r.IsChecked)).Should().Equal(
                ("local one", Strings.Get("FootnotePlacement_Local"), false),
                ("local two", Strings.Get("FootnotePlacement_Local"), false),
                ("global one", Strings.Get("FootnotePlacement_Global"), false));
            sut.Rows.Select(r => r.Marker).Should().Equal("[1]", "[2]", "[1]");
            sut.CanApply.Should().BeFalse();
            sut.IsGlobalFileEnabled.Should().BeFalse();
            sut.Summary.Should().Be(Strings.Format("FootnotePlacementWindow_Summary", 0, 3, 0, 0));
        }
    }

    [Fact]
    public void The_notes_file_defaults_to_the_notes_file_of_the_book_else_to_a_file_with_its_name_else_to_a_new_one()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        Book bare = BookCreator.CreateNewBook("3.0");
        (Book withNotes, HtmlResource _) = NewBook();
        Book named = BookCreator.CreateNewBook("3.0");
        using (bare)
        using (withNotes)
        using (named)
        {
            HtmlResource przypisy = named.CreateEmptyHtmlFile();
            przypisy.RenameTo(FootnoteEditor.GlobalFileName);

            // Act
            FootnotePlacementViewModel none = new([], bare.GetHtmlResourcesExcludingNav(), null, (_, _) => { }, (_, _) => Task.FromResult<PlacementOutcome?>(null));
            FootnotePlacementViewModel current = NewViewModel(withNotes);
            FootnotePlacementViewModel byName = new([], named.GetHtmlResourcesExcludingNav(), null, (_, _) => { }, (_, _) => Task.FromResult<PlacementOutcome?>(null));

            // Assert
            none.SelectedGlobalFile.File.Should().BeNull("the file is made when the notes move");
            none.SelectedGlobalFile.Display.Should().Be(Strings.Format("FootnotePlacementWindow_NewFile", FootnoteEditor.GlobalFileName));
            none.GlobalFiles.Should().HaveCount(1 + bare.GetHtmlResourcesExcludingNav().Count);
            current.SelectedGlobalFile.File.Should().BeSameAs(FootnoteEditor.FindGlobalFile(withNotes));
            current.GlobalFiles.Select(c => c.File).Distinct().Should().HaveCount(current.GlobalFiles.Count, "a file is offered once");
            byName.SelectedGlobalFile.File.Should().BeSameAs(przypisy);
        }
    }

    [Fact]
    public void Select_all_checks_the_footnotes_that_can_move_and_the_summary_counts_both_directions()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            // A global note with references from two files cannot move.
            HtmlResource other = book.CreateEmptyHtmlFile();
            other.SetText(Xhtml(Body));
            Footnote global = FootnoteScanner.Scan(book).Single(f => f.Placement == FootnotePlacement.Global);
            FootnoteEditor.AddReference(book, other, After(other, "Second"), global, out _).Should().Be(FootnoteError.None);
            FootnoteEditor.Add(book, other, After(other, "First"), "another global", FootnotePlacement.Global).Should().Be(FootnoteError.None);
            FootnotePlacementViewModel sut = NewViewModel(book);
            FootnotePlacementRow blocked = sut.Rows.Single(r => !r.IsEnabled);

            // Act
            sut.SelectAllCommand.Execute(null);

            // Assert
            blocked.Remark.Should().Be(Strings.Get("FootnotePlacementWindow_BlockedSeveralFiles"));
            blocked.Fade.Should().BeLessThan(1);
            sut.Rows.Where(r => r.IsEnabled).Should().OnlyContain(r => r.IsChecked);
            blocked.IsChecked.Should().BeFalse("it cannot be moved");
            sut.ToGlobal.Should().Be(2);
            sut.ToLocal.Should().Be(1);
            sut.IsGlobalFileEnabled.Should().BeTrue();
            sut.CanApply.Should().BeTrue();

            // Act
            blocked.IsChecked = true;

            // Assert
            blocked.IsChecked.Should().BeFalse();

            // Act
            sut.SelectNoneCommand.Execute(null);

            // Assert
            sut.Rows.Should().OnlyContain(r => !r.IsChecked);
            sut.CanApply.Should().BeFalse();
        }
    }

    [Fact]
    public void Apply_is_disabled_while_another_book_operation_is_busy_and_enabled_again_once_it_is_not()
    {
        // Arrange
        (Book book, HtmlResource _) = NewBook();
        using (book)
        {
            FootnotePlacementViewModel sut = NewViewModel(book);
            sut.SelectAllCommand.Execute(null);
            sut.CanApply.Should().BeTrue("a precondition of this test: something is checked");

            // Act
            sut.IsBookOperationBusy = true;

            // Assert
            sut.CanApply.Should().BeFalse();
            sut.ApplyCommand.CanExecute(null).Should().BeFalse();

            // Act
            sut.IsBookOperationBusy = false;

            // Assert
            sut.CanApply.Should().BeTrue();
            sut.ApplyCommand.CanExecute(null).Should().BeTrue();
        }
    }

    [Fact]
    public void The_filter_hides_a_kind_and_unchecks_what_it_hides()
    {
        // Arrange
        (Book book, HtmlResource _) = NewBook();
        using (book)
        {
            FootnotePlacementViewModel sut = NewViewModel(book);
            sut.SelectAllCommand.Execute(null);

            // Act
            sut.ShowGlobal = false;

            // Assert
            sut.Rows.Should().OnlyContain(r => r.Item.Placement == FootnotePlacement.Local).And.HaveCount(2);
            sut.ToLocal.Should().Be(0, "the global footnote is hidden, so it is not checked");
            sut.ToGlobal.Should().Be(2);

            // Act
            sut.ShowGlobal = true;
            sut.ShowLocal = false;

            // Assert
            sut.Rows.Should().ContainSingle().Which.Item.Placement.Should().Be(FootnotePlacement.Global);
            sut.ToGlobal.Should().Be(0, "the local footnotes were hidden before; they are unchecked");
            sut.IsGlobalFileEnabled.Should().BeFalse();

            // Act
            sut.SelectAllCommand.Execute(null);

            // Assert
            sut.ToLocal.Should().Be(1);
            sut.ToGlobal.Should().Be(0);

            // Act
            sut.ShowLocal = true;

            // Assert
            sut.Rows.Should().HaveCount(3);
            sut.ToLocal.Should().Be(1, "what was checked stays checked");
        }
    }

    [Fact]
    public async Task Change_gives_the_checked_footnotes_and_the_chosen_file_and_closes_the_dialog()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook();
        using (book)
        {
            IReadOnlyList<PlacementItem>? applied = null;
            HtmlResource? file = html;
            FootnotePlacementViewModel sut = NewViewModel(book, (items, target) =>
            {
                applied = items;
                file = target;
                return Task.FromResult<PlacementOutcome?>(new PlacementOutcome(items.Count, 0));
            });
            bool done = false;
            sut.Done += (_, _) => done = true;
            sut.Rows[1].IsChecked = true;
            sut.Rows[2].IsChecked = true;
            HtmlResource chosen = book.CreateEmptyHtmlFile();
            sut.SelectedGlobalFile = new GlobalFileChoice(chosen, chosen.BookPath);

            // Act
            await sut.ApplyCommand.ExecuteAsync(null);

            // Assert
            applied.Should().NotBeNull();
            applied!.Select(i => i.Footnote.NoteId).Should().Equal(sut.Rows[1].Item.Footnote.NoteId, sut.Rows[2].Item.Footnote.NoteId);
            file.Should().BeSameAs(chosen, "a local footnote becomes global");
            done.Should().BeTrue();

            // Arrange
            done = false;
            sut.SelectNoneCommand.Execute(null);
            sut.Rows[2].IsChecked = true;

            // Act
            await sut.ApplyCommand.ExecuteAsync(null);

            // Assert
            file.Should().BeNull("the file does not matter when nothing becomes global");
            done.Should().BeTrue();
        }
    }

    [Fact]
    public async Task The_dialog_stays_open_when_the_change_was_not_done()
    {
        // Arrange
        (Book book, HtmlResource _) = NewBook();
        using (book)
        {
            FootnotePlacementViewModel sut = NewViewModel(book, (_, _) => Task.FromResult<PlacementOutcome?>(null));
            bool done = false;
            sut.Done += (_, _) => done = true;
            sut.Rows[0].IsChecked = true;

            // Act
            await sut.ApplyCommand.ExecuteAsync(null);

            // Assert
            done.Should().BeFalse("the user cancelled");
            sut.Rows[0].IsChecked.Should().BeTrue();
        }
    }
}
