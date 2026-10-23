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

/// <summary>Tests of the "Unify Letters of Shared Footnotes" dialog's view model (<see cref="FootnoteLettersViewModel"/>).</summary>
public sealed class FootnoteLettersViewModelTests
{
    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static int After(HtmlResource html, string text) =>
        html.GetText().IndexOf(text, StringComparison.Ordinal) + text.Length;

    // Two shared notes: one written in roman letters (after "One" and "Two"), one in latin (after "Three" and "Four").
    private static (Book Book, HtmlResource Html) BookWithSharedNotes()
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml("<p>One.</p>\n<p>Two.</p>\n<p>Three.</p>\n<p>Four.</p>"));
        FootnoteOptions roman = FootnoteOptions.Default with { LetterStyle = FootnoteLetterStyle.LowerRoman };
        FootnoteEditor.Add(book, html, After(html, "One"), "Roman.", FootnotePlacement.Local, out Footnote? first, roman);
        FootnoteEditor.AddReference(book, html, After(html, "Two"), first!, out _, roman).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, html, After(html, "Three"), "Latin.", FootnotePlacement.Local, out Footnote? second);
        FootnoteEditor.AddReference(book, html, After(html, "Four"), second!, out _).Should().Be(FootnoteError.None);
        return (book, html);
    }

    private static FootnoteLettersViewModel NewViewModel(
        Book book, FootnoteLetterStyle style, List<(IReadOnlyList<LetterItem> Items, FootnoteLetterStyle Style)>? applied = null) =>
        new(style, s => FootnoteEditor.PlanLetters(book, s), (_, _) => { }, (items, s) =>
        {
            applied?.Add((items, s));
            return Task.FromResult(true);
        });

    [Fact]
    public void The_tree_holds_the_notes_in_another_style_with_their_references()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (Book book, HtmlResource html) = BookWithSharedNotes();
        using (book)
        {
            // Act
            FootnoteLettersViewModel sut = NewViewModel(book, FootnoteLetterStyle.LowerLatin);

            // Assert
            FootnoteLetterFileNode file = sut.Roots.Should().ContainSingle().Subject;
            file.File.Should().BeSameAs(html);
            FootnoteLetterNoteNode note = sut.Notes.Should().ContainSingle().Subject;
            note.Title.Should().Be(Strings.Format("FootnoteLettersWindow_NoteTitle", "1", "i, ii", "a, b"));
            note.Place.Should().Be("Roman.");
            note.IsChecked.Should().BeTrue();
            note.Children.Select(c => c.Title).Should().Equal(
                Strings.Format("FootnoteLettersWindow_ReferenceTitle", "[1i]", "[1a]"),
                Strings.Format("FootnoteLettersWindow_ReferenceTitle", "[1ii]", "[1b]"));
            note.Children[0].Place.Should().StartWith("One");
            sut.Summary.Should().Be(Strings.Format("FootnoteLettersWindow_Summary", "1", "1"));
            sut.CanUnify.Should().BeTrue();
        }
    }

    [Fact]
    public void Another_style_lists_other_notes_and_unify_passes_the_checked_ones()
    {
        // Arrange
        (Book book, HtmlResource _) = BookWithSharedNotes();
        using (book)
        {
            List<(IReadOnlyList<LetterItem> Items, FootnoteLetterStyle Style)> applied = new();

            // Act
            FootnoteLettersViewModel sut = NewViewModel(book, FootnoteLetterStyle.LowerLatin, applied);
            bool done = false;
            sut.Done += (_, _) => done = true;

            sut.Style = FootnoteLetterStyle.UpperLatin;

            // Assert
            sut.Notes.Should().HaveCount(2, "neither note is written in upper case");
            sut.Roots[0].Check.Should().BeTrue();

            // Act
            sut.Notes[1].IsChecked = false;

            // Assert
            sut.Roots[0].Check.Should().BeNull();

            // Act
            sut.UnifyCommand.Execute(null);

            // Assert
            applied.Should().ContainSingle();
            applied[0].Style.Should().Be(FootnoteLetterStyle.UpperLatin);
            applied[0].Items.Should().Equal(sut.Notes[0].Item);
            done.Should().BeTrue();

            // Act
            sut.SelectNoneCommand.Execute(null);

            // Assert
            sut.CanUnify.Should().BeFalse();
            sut.UnifyCommand.CanExecute(null).Should().BeFalse();
        }
    }
}
