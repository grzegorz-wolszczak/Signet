using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of the check of the accessibility metadata and of its fix (<see cref="AddAccessibilityMetadataFix"/>).</summary>
public sealed class AccessibilityMetadataTests
{
    private const string NoMetadata = "Validation_Accessibility_NoMetadata";
    private const string NoSummary = "Validation_Accessibility_NoSummary";
    private static readonly string[] All =
        ["schema:accessMode", "schema:accessModeSufficient", "schema:accessibilityFeature", "schema:accessibilityHazard"];

    // A new book has an empty nav; withToc gives it a table of contents with a link.
    private static Book NewBook(string version, string body = "<p>Hello</p>", bool withToc = false, params string[] properties)
    {
        Book book = BookCreator.CreateNewBook(version);
        if (withToc)
        {
            book.GetNavResource()!.SetText(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
                + "<head><title>t</title></head>\n<body>\n<nav epub:type=\"toc\"><ol><li><a href=\"Section0001.xhtml\">One</a></li></ol></nav>\n</body>\n</html>");
        }

        book.GetHtmlResourcesExcludingNav()[0].SetText(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"en\">\n<head>\n<title>t</title>\n</head>\n<body>\n" + body + "\n</body>\n</html>");
        foreach (string property in properties)
        {
            book.GetOpf().AddMetaProperty(property, "x");
        }

        return book;
    }

    private static List<ValidationResult> Results(Book book) =>
        AccessibilityValidator.Validate(book).Where(r => r.Code is NoMetadata or NoSummary).ToList();

    private static IReadOnlyList<string> Values(Book book, string property) => book.GetOpf().GetMetaPropertyValues(property);

    [Fact]
    public void A_book_without_accessibility_metadata_is_reported_with_the_missing_properties_and_a_fix()
    {
        // Arrange
        using Book book = NewBook("3.0");

        // Act
        List<ValidationResult> results = Results(book);

        // Assert
        results.Select(r => r.Code).Should().Equal(NoMetadata, NoSummary);
        results[0].Severity.Should().Be(ValidationSeverity.Warning);
        results[0].BookPath.Should().Be(book.GetOpf().BookPath);
        results[0].Message.Should().ContainAll(All);
        results[0].Fix.Should().BeOfType<AddAccessibilityMetadataFix>();
        results[1].Severity.Should().Be(ValidationSeverity.Info);
        results[1].Fix.Should().BeNull("the summary is written by a person");
    }

    [Fact]
    public void Only_the_missing_properties_are_named_and_a_book_that_has_all_is_left_alone()
    {
        // Arrange
        using Book partial = NewBook("3.0", "<p>x</p>", false, "schema:accessMode", "schema:accessibilityHazard");
        using Book complete = NewBook("3.0", "<p>x</p>", false, [.. All, "schema:accessibilitySummary"]);

        // Act
        List<ValidationResult> partialResults = Results(partial);
        List<ValidationResult> completeResults = Results(complete);

        // Assert
        partialResults[0].Message.Should().Contain("schema:accessModeSufficient").And.Contain("schema:accessibilityFeature")
            .And.NotContain("schema:accessMode,").And.NotContain("schema:accessibilityHazard");
        completeResults.Should().BeEmpty();
    }

    [Fact]
    public void A_refining_meta_does_not_count_as_the_property_of_the_book()
    {
        // Arrange
        using Book book = NewBook("3.0");
        book.GetOpf().AddMetaProperty("schema:accessMode", "textual", refines: "#tle1");

        // Act
        List<ValidationResult> results = Results(book);

        // Assert
        results[0].Message.Should().Contain("schema:accessMode");
        Values(book, "schema:accessMode").Should().BeEmpty("the refining meta is not a primary one");
    }

    [Fact]
    public void An_EPUB_2_book_has_no_accessibility_metadata_to_check()
    {
        // Arrange
        using Book book = NewBook("2.0");

        // Act & Assert
        Results(book).Should().BeEmpty();
    }

    [Fact]
    public void The_fix_for_a_book_of_text_says_textual_with_a_table_of_contents_and_that_the_hazards_are_unknown()
    {
        // Arrange
        using Book book = NewBook("3.0", withToc: true);

        // Act
        bool changed = Results(book)[0].Fix!.Apply(book);

        // Assert
        changed.Should().BeTrue();
        Values(book, "schema:accessMode").Should().Equal("textual");
        Values(book, "schema:accessModeSufficient").Should().Equal("textual");
        Values(book, "schema:accessibilityHazard").Should().Equal("unknown");
        Values(book, "schema:accessibilityFeature").Should().Equal("tableOfContents");
        Values(book, "schema:accessibilitySummary").Should().BeEmpty("the summary is not invented");
    }

    [Fact]
    public void The_fix_for_a_book_with_described_images_adds_visual_and_alternative_text()
    {
        // Arrange
        using Book book = NewBook("3.0", "<p>x</p><img src=\"a.png\" alt=\"a cat\"/><img src=\"b.png\" alt=\"\"/>");

        // Act
        Results(book)[0].Fix!.Apply(book);

        // Assert
        Values(book, "schema:accessMode").Should().Equal("textual", "visual");
        Values(book, "schema:accessibilityFeature").Should().Equal("alternativeText");
        Values(book, "schema:accessModeSufficient").Should().Equal("textual");
    }

    [Fact]
    public void The_fix_for_a_book_with_an_image_without_alt_claims_no_alternative_text()
    {
        // Arrange
        using Book book = NewBook("3.0", "<p>x</p><img src=\"a.png\"/>");

        // Act
        Results(book)[0].Fix!.Apply(book);

        // Assert
        Values(book, "schema:accessMode").Should().Equal("textual", "visual");
        Values(book, "schema:accessibilityFeature").Should().BeEmpty();
        Values(book, "schema:accessModeSufficient").Should().BeEmpty();
        Values(book, "schema:accessibilityHazard").Should().Equal("unknown");
    }

    [Fact]
    public void The_fix_keeps_what_the_book_has_and_does_nothing_twice()
    {
        // Arrange
        using Book book = NewBook("3.0", withToc: true);
        book.GetOpf().AddMetaProperty("schema:accessibilityHazard", "noFlashingHazard");
        ValidationFix fix = Results(book)[0].Fix!;

        // Act
        bool first = fix.Apply(book);
        bool second = fix.Apply(book);

        // Assert
        first.Should().BeTrue();
        second.Should().BeFalse();
        Values(book, "schema:accessibilityHazard").Should().Equal("noFlashingHazard");
        Results(book).Select(r => r.Code).Should().Equal(NoSummary);
    }
}
