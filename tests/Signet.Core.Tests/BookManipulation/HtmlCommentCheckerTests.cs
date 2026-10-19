using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Localization;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of finding the comments XML does not allow (<see cref="HtmlCommentChecker"/>) and of listing them when a book is loaded.</summary>
public sealed class HtmlCommentCheckerTests
{
    [Theory]
    [InlineData("<p>text</p>")]
    [InlineData("<!-- a comment -->")]
    [InlineData("<!-- </aside> -->")]
    [InlineData("<!--\n</aside-->")]
    [InlineData("<!---->")]
    [InlineData("<![CDATA[ <!-- not a comment -- here ]]>")]
    [InlineData("<!-- one --><!-- two -->")]
    public void Allowed_comments_and_text_without_comments_have_no_problems(string text)
    {
        // Act
        System.Collections.Generic.IReadOnlyList<HtmlCommentProblem> problems = HtmlCommentChecker.Find(text);

        // Assert
        problems.Should().BeEmpty();
    }

    [Theory]
    [InlineData("<p>a</p><!-- a <!-- b --> c -->", HtmlCommentProblemKind.Nested, 1, 16)]
    [InlineData("<!-- a -- b -->", HtmlCommentProblemKind.DoubleHyphen, 1, 8)]
    [InlineData("<!-- a --- b -->", HtmlCommentProblemKind.DoubleHyphen, 1, 8)]
    [InlineData("<!-- a --->", HtmlCommentProblemKind.EndsWithHyphen, 1, 8)]
    [InlineData("<p>x</p>\n<!-- never closed", HtmlCommentProblemKind.Unterminated, 2, 1)]
    public void A_comment_xml_does_not_allow_is_reported_with_the_place_of_the_problem(string text, HtmlCommentProblemKind kind, int line, int column)
    {
        // Act
        System.Collections.Generic.IReadOnlyList<HtmlCommentProblem> problems = HtmlCommentChecker.Find(text);

        // Assert
        HtmlCommentProblem problem = problems.Should().ContainSingle().Which;
        problem.Kind.Should().Be(kind);
        (problem.Line, problem.Column).Should().Be((line, column));
    }

    [Fact]
    public void Every_such_comment_is_reported_not_only_the_first_and_the_lines_are_those_of_the_text()
    {
        // Arrange
        string text = "<p>1</p>\n<!-- a -- b -->\n<p>2</p>\n<!-- c <!-- d -->\n<p>3</p>\n<!-- e";

        // Act
        System.Collections.Generic.IReadOnlyList<HtmlCommentProblem> problems = HtmlCommentChecker.Find(text);

        // Assert
        problems.Select(p => (p.Kind, p.Line, p.StartLine)).Should().Equal(
            (HtmlCommentProblemKind.DoubleHyphen, 2, 2),
            (HtmlCommentProblemKind.Nested, 4, 4),
            (HtmlCommentProblemKind.Unterminated, 6, 6));
    }

    [Fact]
    public void A_comment_that_spans_lines_knows_where_it_starts_and_ends()
    {
        // Arrange
        string text = "<p>1</p>\n<!--\nfirst\nsecond -- third\nlast\n-->\n<p>2</p>";

        // Act
        HtmlCommentProblem problem = HtmlCommentChecker.Find(text).Should().ContainSingle().Which;

        // Assert
        (problem.StartLine, problem.Line, problem.EndLine).Should().Be((2, 4, 6));
    }

    private static Book LoadWithChapterComment(TempDir source, TempDir temp, string comment)
    {
        string folder = TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, source.Combine("book"));
        string chapter = Path.Combine(folder, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapter, File.ReadAllText(chapter).Replace("</body>", comment + "\n</body>", StringComparison.Ordinal));
        return new ImportEpub(EpubBuilder.BuildInto(folder, temp)).GetBook();
    }

    [Fact]
    public void A_book_with_a_comment_xml_does_not_allow_lists_it_with_its_place_and_does_not_list_the_same_error_twice()
    {
        // Arrange
        using TempDir source = new();
        using TempDir temp = new();

        // Act
        using Book book = LoadWithChapterComment(source, temp, "<!-- a -- b -->");

        // Assert
        LoadWarning warning = book.LoadWarningDetails.Should()
            .ContainSingle(w => w.Title == CoreStrings.Get("LoadWarningGroup_HtmlBadComments")).Which;
        LoadWarningItem item = warning.Items.Should().ContainSingle().Which;
        item.BookPath.Should().EndWith("chapter1.xhtml");
        item.Reason.Should().Be(CoreStrings.Get("LoadWarningReasonCommentDoubleHyphen"));
        item.Line.Should().BeGreaterThan(0);
        item.Column.Should().BeGreaterThan(0);
        book.LoadWarningDetails.Should().NotContain(w => w.Title == CoreStrings.Get("LoadWarningGroup_HtmlNotWellFormed"),
            "the XML error is that comment, listed once");
        book.LoadWarnings.Should().Contain(warning.Message);
    }

    [Fact]
    public void A_comment_that_hides_a_closing_tag_but_is_legal_xml_is_not_reported()
    {
        // Arrange
        using TempDir source = new();
        using TempDir temp = new();

        // Act
        using Book book = LoadWithChapterComment(source, temp, "<!--\n</aside-->");

        // Assert
        book.LoadWarningDetails.Should().NotContain(w => w.Title == CoreStrings.Get("LoadWarningGroup_HtmlBadComments"));
        book.LoadWarningDetails.Should().NotContain(w => w.Title == CoreStrings.Get("LoadWarningGroup_HtmlNotWellFormed"));
    }
}
