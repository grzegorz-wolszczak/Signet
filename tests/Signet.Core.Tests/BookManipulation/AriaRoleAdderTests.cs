using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of the "epub:type without an ARIA role" check and its fix (<see cref="AriaRoleAdder"/>, <see cref="AddAriaRolesFix"/>).</summary>
public sealed class AriaRoleAdderTests
{
    private const string MissingRole = "Validation_Accessibility_MissingRole";

    private static string Xhtml(string body, string bodyAttributes = "") =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\">\n<head>\n<title>t</title>\n</head>\n"
        + $"<body{bodyAttributes}>\n" + body + "\n</body>\n</html>";

    private static Book NewBook(string version, string body, string bodyAttributes = "")
    {
        Book book = BookCreator.CreateNewBook(version);
        book.GetHtmlResourcesExcludingNav()[0].SetText(Xhtml(body, bodyAttributes));
        return book;
    }

    private static List<ValidationResult> Missing(Book book) =>
        AccessibilityValidator.Validate(book).Where(r => r.Code == MissingRole).ToList();

    // Each case is an element with the role the check should ask for (null: none).
    [Theory]
    [InlineData("<section epub:type=\"chapter\"><h1>c</h1></section>", "doc-chapter")]
    [InlineData("<section epub:type=\"bodymatter chapter\"><h1>c</h1></section>", "doc-chapter")]
    [InlineData("<div epub:type=\"z3998:poem epigraph\">e</div>", "doc-epigraph")]
    [InlineData("<a href=\"#n\" epub:type=\"noteref\">1</a>", "doc-noteref")]
    [InlineData("<aside epub:type=\"footnote\" id=\"n\">n</aside>", "doc-footnote")]
    [InlineData("<span epub:type=\"pagebreak\" id=\"p1\" title=\"1\"/>", "doc-pagebreak")]
    [InlineData("<hr epub:type=\"pagebreak\" title=\"2\"/>", "doc-pagebreak")]
    [InlineData("<a id=\"p3\" epub:type=\"pagebreak\" title=\"3\"/>", "doc-pagebreak")]
    [InlineData("<h2 epub:type=\"subtitle\">s</h2>", "doc-subtitle")]
    [InlineData("<figure epub:type=\"pullquote\">q</figure>", "doc-pullquote")]
    [InlineData("<nav epub:type=\"toc\"><ol><li><a href=\"a.xhtml\">a</a></li></ol></nav>", "doc-toc")]
    [InlineData("<nav epub:type=\"page-list\"><ol><li><a href=\"a.xhtml\">a</a></li></ol></nav>", "doc-pagelist")]
    [InlineData("<p epub:type=\"footnote\" role=\"note\">n</p>", null)]
    [InlineData("<section epub:type=\"titlepage\">t</section>", null)]
    [InlineData("<section epub:type=\"cover\">t</section>", null)]
    [InlineData("<ol epub:type=\"endnotes\"><li epub:type=\"endnote\">e</li></ol>", null)]
    [InlineData("<a href=\"#p\" epub:type=\"pagebreak\">4</a>", null)]
    [InlineData("<figure epub:type=\"pullquote\"><figcaption>c</figcaption></figure>", null)]
    [InlineData("<li epub:type=\"footnote\">n</li>", null)]
    [InlineData("<p>no type</p>", null)]
    public void The_role_is_asked_for_only_where_the_epub_type_has_one_that_is_allowed_on_the_element(string element, string? role)
    {
        // Arrange
        using Book book = NewBook("3.0", element);

        // Act
        List<ValidationResult> results = Missing(book);

        // Assert
        if (role is null)
        {
            results.Should().BeEmpty();
        }
        else
        {
            results.Should().ContainSingle().Which.Message.Should().Contain($"role=\"{role}\"");
        }
    }

    [Fact]
    public void The_result_has_the_line_of_the_element_and_a_fix()
    {
        // Arrange
        using Book book = NewBook("3.0", "<p>a</p>\n<a href=\"#n\" epub:type=\"noteref\">1</a>");

        // Act
        ValidationResult result = Missing(book).Single();

        // Assert
        result.Severity.Should().Be(ValidationSeverity.Info);
        result.Line.Should().Be(9, "the body starts on line 7, <p> is on line 8 and the link on line 9");
        result.Fix.Should().BeOfType<AddAriaRolesFix>();
    }

    [Fact]
    public void The_fix_adds_the_roles_of_the_whole_file_and_keeps_what_the_author_wrote()
    {
        // Arrange
        string body = string.Join(
            "\n",
            "<section epub:type=\"chapter\"><h1>c</h1></section>",
            "<a href=\"#n\" epub:type=\"noteref\" id=\"r\">1</a>",
            "<p epub:type=\"footnote\" role=\"note\">n</p>",
            "<aside epub:type=\"footnote\" role=\"\">m</aside>",
            "<hr epub:type=\"pagebreak\"/>");
        using Book book = NewBook("3.0", body, " epub:type=\"chapter\"");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        List<ValidationResult> results = Missing(book);

        // Act
        bool changed = results[0].Fix!.Apply(book);

        // Assert
        results.Should().HaveCount(4);
        changed.Should().BeTrue();
        string text = html.GetText();
        text.Should().Contain("<section role=\"doc-chapter\" epub:type=\"chapter\">")
            .And.Contain("<a role=\"doc-noteref\" href=\"#n\" epub:type=\"noteref\" id=\"r\">")
            .And.Contain("<p epub:type=\"footnote\" role=\"note\">")
            .And.Contain("<aside epub:type=\"footnote\" role=\"doc-footnote\">")
            .And.Contain("<hr role=\"doc-pagebreak\" epub:type=\"pagebreak\"/>")
            .And.Contain("<body epub:type=\"chapter\">", "the body is not a section: doc-chapter is not allowed on it");

        // Act
        bool again = results[1].Fix!.Apply(book);

        // Assert
        again.Should().BeFalse("the first fix already did the whole file");
        Missing(book).Should().BeEmpty();
    }

    [Fact]
    public void An_EPUB_2_book_is_not_checked_because_the_role_attribute_is_not_valid_in_it()
    {
        // Arrange
        using Book book = NewBook("2.0", "<section epub:type=\"chapter\">c</section>");

        // Act & Assert
        Missing(book).Should().BeEmpty();
    }
}
