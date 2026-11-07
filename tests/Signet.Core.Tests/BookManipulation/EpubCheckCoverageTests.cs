using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// What replaced Signet's own checks: for each rule Check Book no longer makes itself, a book that has the problem and the
/// EPUBCheck message that reports it. The checks were retired only once EPUBCheck was seen to report the same situation, so a
/// test here failing means a rule has lost its coverage.
/// </summary>
public sealed class EpubCheckCoverageTests
{
    // A 1x1 PNG (the corpus cover).
    private static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head>\n<title>t</title>\n</head>\n<body>\n"
        + body + "\n</body>\n</html>";

    private static (Book Book, HtmlResource Html) NewBook(string body, string version = "3.0")
    {
        Book book = BookCreator.CreateNewBook(version);
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(body));
        return (book, html);
    }

    private static IReadOnlyList<ValidationResult> Check(Book book) => EpubCheckValidator.Validate(book, culture: s_english);

    private static Resource AddFile(Book book, TempDir temp, string name, byte[] content)
    {
        string path = temp.Combine(name);
        File.WriteAllBytes(path, content);
        return book.AddExistingFiles(new[] { path })[0];
    }

    // The OPF changed through the model, as the old tests of the OPF checks did.
    private static void ChangeOpf(Book book, Action<OpfDocument> change)
    {
        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        change(document);
        opf.SetOpfDocument(document);
    }

    private static void ShouldReport(Book book, string code, string path, string? messagePart = null, ValidationSeverity atLeast = ValidationSeverity.Error)
    {
        IReadOnlyList<ValidationResult> results = Check(book);
        results.Should().Contain(
            r => r.Code == code && r.BookPath == path && r.Severity >= atLeast
                && (messagePart == null || r.Message.Contains(messagePart, StringComparison.Ordinal)),
            "EPUBCheck must report {0} at {1}; it reported: {2}", code, path, string.Join(" | ", results.Select(r => r.Code + "@" + r.BookPath + " " + r.Message)));
    }

    // ---- well-formedness ----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_file_that_is_not_well_formed_is_a_fatal_error_with_a_line()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p>not closed");
        using (book)
        {
            // Act
            ValidationResult[] results = Check(book).Where(r => r.BookPath == html.BookPath).ToArray();

            // Assert
            results.Should().Contain(r => r.Code == "RSC-016" && r.Severity == ValidationSeverity.Fatal && r.Line > 0);
        }
    }

    // ---- the OPF ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_missing_unique_identifier_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Metadata.RemoveAll(m => m.Name == "dc:identifier"));

        // Act & Assert
        ShouldReport(book, "OPF-030", book.GetOpf().BookPath);
    }

    [Fact]
    public void An_empty_unique_identifier_attribute_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Package.UniqueIdentifier = string.Empty);

        // Act & Assert
        ShouldReport(book, "OPF-048", book.GetOpf().BookPath);
    }

    [Fact]
    public void An_empty_identifier_value_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Metadata.Single(m => m.Attributes.Value("id") == d.UniqueIdentifierId).Content = string.Empty);

        // Act & Assert
        ShouldReport(book, "RSC-005", book.GetOpf().BookPath, "dc:identifier");
    }

    [Fact]
    public void Empty_metadata_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Metadata.Clear());

        // Act & Assert
        ShouldReport(book, "RSC-005", book.GetOpf().BookPath, "metadata");
    }

    [Fact]
    public void An_empty_manifest_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Manifest.Clear());

        // Act & Assert
        ShouldReport(book, "RSC-005", book.GetOpf().BookPath, "manifest");
    }

    [Fact]
    public void An_empty_spine_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Spine.Clear());

        // Act & Assert
        ShouldReport(book, "RSC-005", book.GetOpf().BookPath, "spine");
    }

    [Fact]
    public void A_manifest_item_without_an_id_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Manifest[0].Id = string.Empty);

        // Act & Assert
        ShouldReport(book, "RSC-005", book.GetOpf().BookPath, "\"id\"");
    }

    [Fact]
    public void A_duplicate_manifest_id_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Manifest[0].Id = d.Manifest[1].Id);

        // Act & Assert
        ShouldReport(book, "RSC-005", book.GetOpf().BookPath, "Duplicate");
    }

    [Fact]
    public void A_duplicate_manifest_href_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Manifest[0].Href = d.Manifest[1].Href);

        // Act & Assert
        ShouldReport(book, "OPF-074", book.GetOpf().BookPath, atLeast: ValidationSeverity.Warning);
    }

    [Fact]
    public void An_itemref_to_nothing_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Spine[0].IdRef = "does-not-exist-in-manifest");

        // Act & Assert
        ShouldReport(book, "OPF-049", book.GetOpf().BookPath, "does-not-exist-in-manifest");
    }

    [Fact]
    public void An_itemref_without_an_idref_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Spine[0].IdRef = string.Empty);

        // Act & Assert
        ShouldReport(book, "OPF-049", book.GetOpf().BookPath);
    }

    [Fact]
    public void More_than_one_cover_image_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d =>
        {
            d.Manifest[0].Attributes.Set("properties", "cover-image");
            d.Manifest[1].Attributes.Set("properties", "cover-image");
        });

        // Act & Assert
        ShouldReport(book, "RSC-005", book.GetOpf().BookPath, "cover-image");
    }

    [Fact]
    public void An_epub3_book_without_a_navigation_document_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        ChangeOpf(book, d => d.Manifest.RemoveAll(m => m.Attributes.Value("properties").Contains("nav", StringComparison.Ordinal)));

        // Act & Assert
        ShouldReport(book, "RSC-005", book.GetOpf().BookPath, "nav");
    }

    [Fact]
    public void An_epub2_book_without_an_ncx_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("2.0");
        book.GetFolderKeeper().RemoveNcxFromFolder();

        // Act & Assert
        ShouldReport(book, "OPF-049", book.GetOpf().BookPath);
    }

    // ---- links and references -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_link_to_a_missing_file_is_reported_with_its_line()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p><a href=\"missing.xhtml\">x</a></p>");
        using (book)
        {
            // Act & Assert
            Check(book).Should().Contain(r => r.Code == "RSC-007" && r.BookPath == html.BookPath && r.Line > 0);
        }
    }

    [Fact]
    public void A_fragment_that_is_not_in_the_other_document_is_reported()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(string.Empty);
        using (book)
        {
            HtmlResource other = book.CreateEmptyHtmlFile();
            html.SetText(Xhtml($"<p><a href=\"{Core.BookPath.Relative(html.BookPath, other.BookPath)}#nope\">x</a></p>"));

            // Act & Assert
            ShouldReport(book, "RSC-012", html.BookPath);
        }
    }

    [Fact]
    public void A_fragment_that_is_not_in_the_same_document_is_reported()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p><a href=\"#nope\">x</a></p>");
        using (book)
        {
            // Act & Assert
            ShouldReport(book, "RSC-012", html.BookPath);
        }
    }

    [Fact]
    public void A_css_url_to_a_missing_file_is_reported()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        CssResource css = book.CreateEmptyCssFile();
        css.SetText("body { background: url('missing.png'); }");

        // Act & Assert
        ShouldReport(book, "RSC-007", css.BookPath);
    }

    [Fact]
    public void A_link_to_a_file_the_manifest_lacks_is_reported()
    {
        // Arrange
        using TempDir temp = new();
        (Book book, HtmlResource html) = NewBook(string.Empty);
        using (book)
        {
            Resource image = AddFile(book, temp, "Pic.png", Png1x1);
            ChangeOpf(book, d => d.Manifest.RemoveAll(m => m.Href.EndsWith("Pic.png", StringComparison.Ordinal)));
            html.SetText(Xhtml($"<p><img src=\"{Core.BookPath.Relative(html.BookPath, image.BookPath)}\" alt=\"\"/></p>"));

            // Act & Assert
            ShouldReport(book, "RSC-008", html.BookPath);
        }
    }

    [Fact]
    public void A_css_url_to_a_file_the_manifest_lacks_is_reported()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("3.0");
        Resource image = AddFile(book, temp, "Pic.png", Png1x1);
        ChangeOpf(book, d => d.Manifest.RemoveAll(m => m.Href.EndsWith("Pic.png", StringComparison.Ordinal)));
        CssResource css = book.CreateEmptyCssFile();
        css.SetText($"body {{ background: url('{Core.BookPath.Relative(css.BookPath, image.BookPath)}'); }}");

        // Act & Assert
        ShouldReport(book, "RSC-008", css.BookPath, atLeast: ValidationSeverity.Warning);
    }

    [Fact]
    public void A_reference_with_the_wrong_letter_case_is_reported_as_a_missing_file()
    {
        // Arrange
        using TempDir temp = new();
        (Book book, HtmlResource html) = NewBook(string.Empty);
        using (book)
        {
            Resource image = AddFile(book, temp, "Pic.png", Png1x1);
            string wrong = Core.BookPath.Relative(html.BookPath, image.BookPath).Replace("Pic.png", "pic.png", StringComparison.Ordinal);
            html.SetText(Xhtml($"<p><img src=\"{wrong}\" alt=\"\"/></p>"));

            // Act & Assert
            ShouldReport(book, "RSC-007", html.BookPath);
        }
    }

    // ---- ids ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_id_that_is_not_unique_in_its_file_is_reported_on_both_elements()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p id=\"a\">1</p>\n<p id=\"a\">2</p>");
        using (book)
        {
            // Act
            ValidationResult[] duplicates = Check(book).Where(r => r.Code == "RSC-005" && r.Message.Contains("Duplicate ID", StringComparison.Ordinal)).ToArray();

            // Assert
            duplicates.Select(r => r.Line).Should().Equal(8, 9);
            duplicates.Should().OnlyContain(r => r.BookPath == html.BookPath);
        }
    }
}
