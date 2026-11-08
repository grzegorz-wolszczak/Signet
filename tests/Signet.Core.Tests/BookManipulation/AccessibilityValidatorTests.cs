using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
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
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head>\n<title>t</title>\n</head>\n<body>\n"
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
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" aria-labelledby=\"t\"><image href=\"a.png\"/></svg>", false)]
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
}
