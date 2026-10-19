using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Resources;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests of the "Add Reference to Footnote" window's view model (<see cref="FootnoteReferenceViewModel"/>).</summary>
public sealed class FootnoteReferenceViewModelTests
{
    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>";

    private static int After(HtmlResource html, string text) =>
        html.GetText().IndexOf(text, StringComparison.Ordinal) + text.Length;

    // A file with local notes after "One" and "Three" and a global one after "Two"; the position is after "Four".
    private static (Book Book, ReferenceTargetList Targets) BookWithNotes()
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml("<p>One.</p>\n<p>Two.</p>\n<p>Three.</p>\n<p>Four.</p>"));
        FootnoteEditor.Add(book, html, After(html, "One"), "Apples are red.", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, html, After(html, "Two"), "Bananas are yellow.", FootnotePlacement.Global).Should().Be(FootnoteError.None);
        FootnoteEditor.Add(book, html, After(html, "Three"), "Cherries are red.", FootnotePlacement.Local).Should().Be(FootnoteError.None);
        return (book, FootnoteEditor.ReferenceTargets(book, html, After(html, "Four")));
    }

    [Fact]
    public void The_rows_show_the_notes_and_the_nearest_one_before_the_caret_is_chosen()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (Book book, ReferenceTargetList targets) = BookWithNotes();
        using (book)
        {
            // Act
            FootnoteReferenceViewModel sut = new(targets);

            // Assert
            sut.Visible.Select(r => (r.Number, r.Kind, r.Text)).Should().Equal(
                ("[1]", Strings.Get("FootnoteConversionWindow_Local"), "Apples are red."),
                ("[2]", Strings.Get("FootnoteConversionWindow_Local"), "Cherries are red."),
                ("[1]", Strings.Get("FootnoteConversionWindow_Global"), "Bananas are yellow."));
            sut.Visible[0].File.Should().BeEmpty("a local note is in this file");
            sut.Visible[2].File.Should().Be(book.GetHtmlResourcesExcludingNav()[0].Filename);
            sut.Selected.Should().BeSameAs(sut.Visible[1], "the reference of the cherries is the nearest before the caret");
            sut.CanAccept.Should().BeTrue();
            sut.Summary.Should().Be(Strings.Format("FootnoteReferenceWindow_Summary", "3", "3"));
        }
    }

    [Fact]
    public void The_filter_keeps_the_matching_rows_and_the_choice_when_it_matches()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        (Book book, ReferenceTargetList targets) = BookWithNotes();
        using (book)
        {
            FootnoteReferenceViewModel sut = new(targets);
            FootnoteReferenceRow cherries = sut.Selected!;

            // Act
            sut.Filter = "RED";

            // Assert
            sut.Visible.Select(r => r.Text).Should().Equal("Apples are red.", "Cherries are red.");
            sut.Selected.Should().BeSameAs(cherries);

            // Act
            sut.Filter = "banana";

            // Assert
            sut.Visible.Should().ContainSingle().Which.Text.Should().Be("Bananas are yellow.");
            sut.Selected.Should().BeSameAs(sut.Visible[0], "the choice moves to the first matching row");

            // Act
            sut.Filter = "kiwi";

            // Assert
            sut.Visible.Should().BeEmpty();
            sut.Selected.Should().BeNull();
            sut.CanAccept.Should().BeFalse();
            sut.Summary.Should().Be(Strings.Format("FootnoteReferenceWindow_Summary", "0", "3"));
        }
    }
}
