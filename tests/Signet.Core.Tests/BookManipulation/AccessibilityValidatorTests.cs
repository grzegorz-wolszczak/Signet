using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="AccessibilityValidator"/> — images without a text alternative.</summary>
public sealed class AccessibilityValidatorTests
{
    private const string SvgNoAlt = "Validation_Accessibility_SvgNoAlt";
    private const string ImageNoAlt = "Validation_Accessibility_ImageNoAlt";
    private static readonly string[] Skip = { ImageNoAlt };

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"en\">\n<head>\n<title>t</title>\n</head>\n<body>\n"
        + body + "\n</body>\n</html>";

    private static List<ValidationResult> Validate(string body, out int line)
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        book.GetHtmlResourcesExcludingNav()[0].SetText(Xhtml(body));
        List<ValidationResult> results = AccessibilityValidator.Validate(book).ToList();
        line = results.Count > 0 ? results[0].Line : -1;
        return results;
    }

    private static List<string> Codes(string body) => Validate(body, out _).Select(r => r.Code).ToList();

    [Theory]
    [InlineData("<img src=\"a.png\"/>")]
    [InlineData("<p>x</p><IMG src=\"a.png\" class=\"c\">")]
    public void An_image_without_alt_is_reported(string body)
    {
        // Act
        List<string> codes = Codes(body);

        // Assert
        codes.Should().Equal(ImageNoAlt);
    }

    [Theory]
    [InlineData("<img src=\"a.png\" alt=\"A cat\"/>")]
    [InlineData("<img src=\"a.png\" alt=\"\"/>")]
    [InlineData("<img src=\"a.png\" aria-label=\"A cat\"/>")]
    [InlineData("<img src=\"a.png\" role=\"presentation\"/>")]
    [InlineData("<img src=\"a.png\" role=\"none\"/>")]
    [InlineData("<img src=\"a.png\" aria-hidden=\"true\"/>")]
    public void An_image_with_a_text_alternative_or_marked_as_decoration_is_not_reported(string body)
    {
        // Act
        List<string> codes = Codes(body);

        // Assert
        codes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"a.png\"/></svg>", true)]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><title>Cover</title><image href=\"a.png\"/></svg>", false)]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" aria-label=\"Cover\"><image href=\"a.png\"/></svg>", false)]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" aria-labelledby=\"t\"><image href=\"a.png\"/></svg><p id=\"t\">title</p>", false)]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" role=\"presentation\"><image href=\"a.png\"/></svg>", false)]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" aria-hidden=\"true\"><image href=\"a.png\"/></svg>", false)]
    public void An_svg_needs_a_title_or_a_label(string body, bool reported)
    {
        // Act
        List<string> codes = Codes(body);

        // Assert
        codes.Count.Should().Be(reported ? 1 : 0);
        codes.Should().OnlyContain(c => c == SvgNoAlt);
    }

    [Fact]
    public void A_nested_svg_is_judged_with_its_outer_svg_and_the_line_is_given()
    {
        // Arrange
        string body = "<p>a</p>\n<p>b</p>\n<svg xmlns=\"http://www.w3.org/2000/svg\"><svg><image href=\"a.png\"/></svg></svg>";

        // Act
        List<ValidationResult> results = Validate(body, out int line);

        // Assert
        results.Should().ContainSingle();
        line.Should().Be(10, "the body starts on line 8 of the file");
    }

    [Fact]
    public void The_check_is_part_of_the_book_validation_and_can_be_skipped()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        book.GetHtmlResourcesExcludingNav()[0].SetText(Xhtml("<img src=\"a.png\"/>"));

        // Act
        IReadOnlyList<ValidationResult> all = BookValidator.ValidateCurrentBook(book);
        IReadOnlyList<ValidationResult> skipped = BookValidator.ValidateCurrentBook(book, Skip);

        // Assert
        all.Should().Contain(r => r.Code == "Validation_Accessibility_ImageNoAlt" && r.Severity == ValidationSeverity.Warning);
        skipped.Should().NotContain(r => r.Code == "Validation_Accessibility_ImageNoAlt");
    }

    private const string NoLanguage = "Validation_Accessibility_NoLanguage";

    private static string WithRoot(string root) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" + root + "\n<head><title>t</title></head>\n<body><p>a</p></body>\n</html>";

    [Theory]
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\">", true)]
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"pl\">", false)]
    [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\" xml:lang=\"pl\">", false)]
    public void A_file_without_a_language_is_reported(string root, bool reported)
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        book.GetHtmlResourcesExcludingNav()[0].SetText(WithRoot(root));

        // Act
        List<ValidationResult> results = AccessibilityValidator.Validate(book).Where(r => r.Code == NoLanguage).ToList();

        // Assert
        results.Count.Should().Be(reported ? 1 : 0);
    }

    [Theory]
    [InlineData("3.0", " lang=\"en\" xml:lang=\"en\"")]
    [InlineData("2.0", " xml:lang=\"en\"")]
    public void The_fix_gives_the_file_the_language_of_the_book_and_keeps_its_other_attributes(string version, string expected)
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook(version);
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(WithRoot("<html xmlns=\"http://www.w3.org/1999/xhtml\">"));
        ValidationResult result = AccessibilityValidator.Validate(book).Single(r => r.Code == NoLanguage);

        // Act
        bool applied = result.Fix!.Apply(book);

        // Assert
        applied.Should().BeTrue();
        html.GetText().Should().Contain("<html" + expected + " xmlns=\"http://www.w3.org/1999/xhtml\">");
        AccessibilityValidator.Validate(book).Should().NotContain(r => r.Code == NoLanguage);
        result.Fix.Apply(book).Should().BeFalse("there is nothing left to change");
    }

    private const string HeadingSkip = "Validation_Accessibility_HeadingSkip";

    private static List<ValidationResult> Headings(params string[] bodies)
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        book.GetHtmlResourcesExcludingNav()[0].SetText(Xhtml(bodies[0]));
        for (int i = 1; i < bodies.Length; i++)
        {
            HtmlResource next = book.CreateEmptyHtmlFile();
            next.SetText(Xhtml(bodies[i]));
        }

        return AccessibilityValidator.Validate(book).Where(r => r.Code == HeadingSkip).ToList();
    }

    [Theory]
    [InlineData("<h1>a</h1><h2>b</h2><h3>c</h3>", 0)]
    [InlineData("<h1>a</h1><h3>b</h3>", 1)]
    [InlineData("<h2>a</h2><h4>b</h4><h2>c</h2><h1>d</h1>", 1)]
    [InlineData("<h3>first</h3><h2>up</h2>", 0)]
    [InlineData("<H1>a</H1><H4>b</H4>", 1)]
    [InlineData("<h1>a</h1><hr/><header>x</header><h2>b</h2>", 0)]
    public void A_heading_may_not_skip_a_level_inside_a_file(string body, int reported)
    {
        // Act
        List<ValidationResult> results = Headings(body);

        // Assert
        results.Count.Should().Be(reported);
    }

    [Fact]
    public void A_heading_is_judged_against_the_last_one_of_the_previous_file_in_reading_order()
    {
        // Act — a part page with an h2, then a chapter starting with h4.
        List<ValidationResult> results = Headings("<h1>Book</h1><h2>Part</h2>", "<h4>Chapter</h4>");

        // Assert
        results.Should().ContainSingle();
        results[0].Message.Should().Contain("h4").And.Contain("h2");
    }

    [Fact]
    public void Headings_in_files_outside_the_spine_are_not_part_of_the_reading_order()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        book.GetHtmlResourcesExcludingNav()[0].SetText(Xhtml("<h1>a</h1>"));
        HtmlResource loose = book.CreateEmptyHtmlFile();
        loose.SetText(Xhtml("<h5>x</h5>"));
        book.GetOpf().RemoveResourceFromSpine(loose);

        // Act
        List<ValidationResult> results = AccessibilityValidator.Validate(book).Where(r => r.Code == HeadingSkip).ToList();

        // Assert
        results.Should().BeEmpty();
    }
}
