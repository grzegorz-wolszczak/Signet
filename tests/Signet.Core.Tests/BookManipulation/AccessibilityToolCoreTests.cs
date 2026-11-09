using System;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of the operations of Tools → Accessibility: the language of the book and of its files, and the accessibility metadata.</summary>
public sealed class AccessibilityToolCoreTests
{
    private static string Xhtml(string htmlAttributes) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + $"<html xmlns=\"http://www.w3.org/1999/xhtml\"{htmlAttributes}>\n<head>\n<title>t</title>\n</head>\n<body>\n<p>x</p>\n</body>\n</html>";

    // --- language of the book ---

    [Fact]
    public void The_languages_of_the_book_are_its_dc_language_values_in_order()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");

        // Act
        AccessibilityLanguage.SetLanguages(book, ["pl", "en"]);

        // Assert
        AccessibilityLanguage.GetLanguages(book).Should().Equal("pl", "en");
        book.GetMetadataValues("dc:language").Should().Equal("pl", "en");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void Setting_the_languages_keeps_the_other_metadata_and_refuses_none()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        string[] titles = book.GetMetadataValues("dc:title").ToArray();

        // Act
        AccessibilityLanguage.SetLanguages(book, ["de", " de ", "", "fr"]);

        // Assert
        book.GetMetadataValues("dc:title").Should().Equal(titles);
        AccessibilityLanguage.GetLanguages(book).Should().Equal("de", "fr");
        FluentActions.Invoking(() => AccessibilityLanguage.SetLanguages(book, ["", "  "])).Should().Throw<ArgumentException>();
    }

    // --- language of the files ---

    [Fact]
    public void The_files_without_a_language_get_the_main_language_of_the_book_and_the_others_are_left_alone()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        AccessibilityLanguage.SetLanguages(book, ["pl", "en"]);
        HtmlResource first = book.GetHtmlResourcesExcludingNav()[0];
        HtmlResource second = book.CreateEmptyHtmlFile();
        first.SetText(Xhtml(string.Empty));
        second.SetText(Xhtml(" lang=\"en\" xml:lang=\"en\""));

        // Act
        int changed = AccessibilityLanguage.SetHtmlLanguages(book, onlyMissing: true);

        // Assert
        first.GetText().Should().Contain("<html lang=\"pl\" xml:lang=\"pl\" xmlns=");
        second.GetText().Should().Contain("lang=\"en\" xml:lang=\"en\"").And.NotContain("\"pl\"");
        changed.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Every_file_can_be_given_the_main_language_replacing_the_one_it_had()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        AccessibilityLanguage.SetLanguages(book, ["pl"]);
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(" lang=\"en\" xml:lang=\"de-DE\" class=\"c\""));

        // Act
        AccessibilityLanguage.SetHtmlLanguages(book, onlyMissing: false);

        // Assert
        string text = html.GetText();
        text.Should().Contain("lang=\"pl\"").And.Contain("xml:lang=\"pl\"").And.Contain("class=\"c\"").And.NotContain("\"en\"").And.NotContain("de-DE");
        AccessibilityLanguage.SetHtmlLanguages(book, onlyMissing: false).Should().Be(0, "nothing is left to change");
    }

    [Fact]
    public void In_an_epub2_book_only_xml_lang_is_written()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("2.0");
        AccessibilityLanguage.SetLanguages(book, ["pl"]);
        HtmlResource html = book.GetHtmlResources()[0];
        html.SetText(Xhtml(string.Empty));

        // Act
        AccessibilityLanguage.SetHtmlLanguages(book, onlyMissing: true);

        // Assert
        html.GetText().Should().Contain("xml:lang=\"pl\"").And.NotContain(" lang=\"pl\"");
    }

    // --- accessibility metadata ---

    [Fact]
    public void The_metadata_is_read_and_written_as_lists_of_values_and_a_summary()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        AccessibilityMetadataValues values = new()
        {
            Summary = "All images have alternative text & the book has a table of contents.",
            AccessModes = ["textual", "visual"],
            AccessModesSufficient = ["textual"],
            Features = ["alternativeText", "tableOfContents"],
            Hazards = ["none"],
            ConformsTo = ["EPUB Accessibility 1.1 - WCAG 2.1 Level AA"],
        };

        // Act
        AccessibilityMetadataEditor.Write(book, values);
        AccessibilityMetadataValues read = AccessibilityMetadataEditor.Read(book);

        // Assert
        read.Summary.Should().Be(values.Summary);
        read.AccessModes.Should().Equal("textual", "visual");
        read.AccessModesSufficient.Should().Equal("textual");
        read.Features.Should().Equal("alternativeText", "tableOfContents");
        read.Hazards.Should().Equal("none");
        read.ConformsTo.Should().Equal("EPUB Accessibility 1.1 - WCAG 2.1 Level AA");
        book.GetOpf().GetMetaPropertyValues("schema:accessMode").Should().Equal("textual", "visual");
        book.GetOpf().GetMetaPropertyValues("dcterms:conformsTo").Should().ContainSingle();
    }

    [Fact]
    public void Writing_replaces_the_old_values_leaves_other_metadata_and_changes_nothing_when_nothing_changed()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        book.GetOpf().AddMetaProperty("schema:accessMode", "auditory");
        book.GetOpf().AddMetaProperty("schema:accessMode", "tactile");
        book.GetOpf().AddMetaProperty("schema:accessMode", "ignored", refines: "#tle1");
        string[] titles = book.GetMetadataValues("dc:title").ToArray();

        // Act
        AccessibilityMetadataEditor.Write(book, new AccessibilityMetadataValues { AccessModes = ["textual"] });

        // Assert
        book.GetOpf().GetMetaPropertyValues("schema:accessMode").Should().Equal("textual");
        book.GetOpf().GetText().Should().Contain("refines=\"#tle1\"", "a refining meta is not the property of the book");
        book.GetMetadataValues("dc:title").Should().Equal(titles);

        // Act
        string opf = book.GetOpf().GetText();
        AccessibilityMetadataEditor.Write(book, new AccessibilityMetadataValues { AccessModes = ["textual"] });

        // Assert
        book.GetOpf().GetText().Should().Be(opf);
    }

    [Fact]
    public void An_empty_summary_and_empty_lists_remove_the_properties()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        AccessibilityMetadataEditor.Write(book, new AccessibilityMetadataValues { Summary = "x", Hazards = ["unknown"] });

        // Act
        AccessibilityMetadataEditor.Write(book, new AccessibilityMetadataValues { Summary = "  " });

        // Assert
        AccessibilityMetadataEditor.Read(book).Summary.Should().BeEmpty();
        book.GetOpf().GetMetaPropertyValues("schema:accessibilityHazard").Should().BeEmpty();
    }

    [Fact]
    public void The_vocabularies_hold_the_values_of_schema_org_that_the_check_and_the_fix_use()
    {
        // Assert
        AccessibilityVocabulary.AccessModes.Should().Contain(["textual", "visual", "auditory", "tactile"]);
        AccessibilityVocabulary.Features.Should().Contain(["alternativeText", "tableOfContents", "readingOrder"]);
        AccessibilityVocabulary.Hazards.Should().Contain(["unknown", "none", "noFlashingHazard"]);
        AccessibilityVocabulary.ConformsTo.Should().Contain(s => s.Contains("WCAG 2.1 Level AA", StringComparison.Ordinal));
        AccessibilityVocabulary.AccessModesSufficient.Should().Contain("textual");
    }
}
