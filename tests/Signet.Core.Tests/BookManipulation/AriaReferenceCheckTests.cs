using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of the check of dangling ARIA references (<see cref="AccessibilityValidator"/>).</summary>
public sealed class AriaReferenceCheckTests
{
    private const string Code = "Validation_Accessibility_AriaReference";

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"en\">\n<head>\n<title>t</title>\n</head>\n<body>\n"
        + body + "\n</body>\n</html>";

    private static List<ValidationResult> Check(string body, string version = "3.0")
    {
        using Book book = BookCreator.CreateNewBook(version);
        book.GetHtmlResourcesExcludingNav()[0].SetText(Xhtml(body));
        return AccessibilityValidator.Validate(book).Where(r => r.Code == Code).ToList();
    }

    [Theory]
    [InlineData("aria-labelledby")]
    [InlineData("aria-describedby")]
    [InlineData("aria-controls")]
    [InlineData("aria-owns")]
    [InlineData("aria-flowto")]
    [InlineData("aria-details")]
    [InlineData("aria-activedescendant")]
    public void Every_attribute_that_refers_to_an_id_is_checked(string attribute)
    {
        // Act
        List<ValidationResult> results = Check($"<div {attribute}=\"nowhere\">x</div>");

        // Assert
        ValidationResult result = results.Should().ContainSingle().Which;
        result.Message.Should().Contain(attribute).And.Contain("nowhere");
        result.Severity.Should().Be(ValidationSeverity.Warning);
        result.Fix.Should().BeNull();
    }

    [Fact]
    public void A_reference_to_an_id_of_the_file_is_fine()
    {
        // Act
        List<ValidationResult> results = Check("<h2 id=\"t\">t</h2><p aria-labelledby=\"t\" aria-describedby=\"t\">x</p>");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void In_a_list_only_the_missing_ids_are_reported_one_by_one()
    {
        // Act
        List<ValidationResult> results = Check("<h2 id=\"a\">a</h2><h3 id=\"b\">b</h3>\n<p aria-labelledby=\"a  gone1\tb gone2\">x</p>");

        // Assert
        results.Should().HaveCount(2);
        results.Select(r => r.Message).Should().Satisfy(
            m => m.Contains("gone1") && !m.Contains("gone2"),
            m => m.Contains("gone2") && !m.Contains("gone1"));
        results.Select(r => r.Line).Should().OnlyContain(l => l == 9, "the paragraph is on line 9");
    }

    [Fact]
    public void An_id_defined_after_the_reference_counts()
    {
        // Act
        List<ValidationResult> results = Check("<p aria-describedby=\"later\">x</p><p id=\"later\">y</p>");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void An_empty_value_and_other_attributes_are_ignored()
    {
        // Act
        List<ValidationResult> results = Check("<p aria-labelledby=\"\" aria-label=\"x\" aria-hidden=\"true\" data-aria-owns=\"zzz\">x</p>");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void An_EPUB_2_book_has_no_aria_to_check()
    {
        // Act
        List<ValidationResult> results = Check("<p aria-labelledby=\"gone\">x</p>", "2.0");

        // Assert
        results.Should().BeEmpty();
    }
}
