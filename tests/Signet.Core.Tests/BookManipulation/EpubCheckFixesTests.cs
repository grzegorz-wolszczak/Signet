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
/// The automatic fixes that go with the problems EPUBCheck reports: the problem is found by EPUBCheck, carries a fix, and once the
/// fix is applied EPUBCheck no longer reports it.
/// </summary>
public sealed class EpubCheckFixesTests
{
    // A 1x1 PNG (the corpus cover).
    private static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head>\n<title>t</title>\n</head>\n<body>\n"
        + body + "\n</body>\n</html>";

    private static (Book Book, HtmlResource Html) NewBook(string body)
    {
        Book book = BookCreator.CreateNewBook("3.0");
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

    [Fact]
    public void A_duplicate_id_is_fixed_by_renaming_the_later_ones()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p id=\"a\">1</p>\n<p id=\"a\">2</p>");
        using (book)
        {
            ValidationResult[] duplicates = Check(book).Where(r => r.Code == "RSC-005" && r.Message.Contains("Duplicate ID", StringComparison.Ordinal)).ToArray();
            duplicates.Should().NotBeEmpty().And.OnlyContain(r => r.Fix != null && r.BookPath == html.BookPath);

            // Act
            duplicates.Select(r => r.Fix!.Apply(book)).Should().Contain(true);

            // Assert
            html.GetText().Should().Contain("<p id=\"a\">1</p>").And.NotContain("<p id=\"a\">2</p>");
            Check(book).Should().NotContain(r => r.Message.Contains("Duplicate ID", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_file_the_manifest_lacks_is_added_to_it()
    {
        // Arrange
        using TempDir temp = new();
        (Book book, HtmlResource html) = NewBook(string.Empty);
        using (book)
        {
            Resource image = AddFile(book, temp, "Pic.png", Png1x1);
            OpfDocument document = book.GetOpf().GetOpfDocument();
            document.Manifest.RemoveAll(m => m.Href.EndsWith("Pic.png", StringComparison.Ordinal));
            book.GetOpf().SetOpfDocument(document);
            html.SetText(Xhtml($"<p><img src=\"{Core.BookPath.Relative(html.BookPath, image.BookPath)}\" alt=\"\"/></p>"));
            ValidationResult problem = Check(book).Single(r => r.Code == "RSC-008");
            problem.Fix.Should().BeOfType<AddToManifestFix>();

            // Act
            problem.Fix!.Apply(book).Should().BeTrue();

            // Assert
            Check(book).Should().NotContain(r => r.Code == "RSC-008");
        }
    }

    [Fact]
    public void A_reference_with_the_wrong_letter_case_is_corrected()
    {
        // Arrange
        using TempDir temp = new();
        (Book book, HtmlResource html) = NewBook(string.Empty);
        using (book)
        {
            Resource image = AddFile(book, temp, "Pic.png", Png1x1);
            string correct = Core.BookPath.Relative(html.BookPath, image.BookPath);
            html.SetText(Xhtml($"<p><img src=\"{correct.Replace("Pic.png", "pic.png", StringComparison.Ordinal)}\" alt=\"\"/></p>"));
            ValidationResult problem = Check(book).Single(r => r.Code == "RSC-007");
            problem.Fix.Should().BeOfType<ReferenceCaseFix>();

            // Act
            problem.Fix!.Apply(book).Should().BeTrue();

            // Assert
            html.GetText().Should().Contain($"src=\"{correct}\"");
            Check(book).Should().NotContain(r => r.Code == "RSC-007");
        }
    }

    [Fact]
    public void A_css_url_with_the_wrong_letter_case_is_corrected()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("3.0");
        Resource image = AddFile(book, temp, "Pic.png", Png1x1);
        CssResource css = book.CreateEmptyCssFile();
        string correct = Core.BookPath.Relative(css.BookPath, image.BookPath);
        css.SetText($"body {{ background: url('{correct.ToLowerInvariant()}'); }}");
        ValidationResult problem = Check(book).Single(r => r.Code == "RSC-007" && r.BookPath == css.BookPath);

        // Act
        problem.Fix!.Apply(book).Should().BeTrue();

        // Assert
        css.GetText().Should().Be($"body {{ background: url('{correct}'); }}");
        Check(book).Should().NotContain(r => r.Code == "RSC-007");
    }

    [Fact]
    public void A_reference_to_a_missing_file_has_no_fix()
    {
        // Arrange
        (Book book, _) = NewBook("<p><img src=\"missing.png\" alt=\"\"/></p>");
        using (book)
        {
            // Act
            ValidationResult problem = Check(book).Single(r => r.Code == "RSC-007");

            // Assert
            problem.Fix.Should().BeNull();
        }
    }

    [Fact]
    public void An_empty_unique_identifier_gets_a_uuid()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        OpfDocument document = book.GetOpf().GetOpfDocument();
        document.Metadata.Single(m => m.Attributes.Value("id") == document.UniqueIdentifierId).Content = string.Empty;
        book.GetOpf().SetOpfDocument(document);
        ValidationResult problem = Check(book).First(r => r.BookPath == book.GetOpf().BookPath && r.Fix is UniqueIdentifierFix);

        // Act
        problem.Fix!.Apply(book).Should().BeTrue();

        // Assert
        book.GetOpf().GetMainIdentifierValue().Should().StartWith("urn:uuid:");
        Check(book).Should().NotContain(r => r.BookPath == book.GetOpf().BookPath && r.Message.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_missing_unique_identifier_is_created()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        OpfDocument document = book.GetOpf().GetOpfDocument();
        document.Metadata.RemoveAll(m => m.Name == "dc:identifier");
        book.GetOpf().SetOpfDocument(document);
        ValidationResult problem = Check(book).Single(r => r.Code == "OPF-030");
        problem.Fix.Should().BeOfType<UniqueIdentifierFix>();

        // Act
        problem.Fix!.Apply(book).Should().BeTrue();

        // Assert
        Check(book).Should().NotContain(r => r.Code == "OPF-030");
        book.GetOpf().GetMainIdentifierValue().Should().StartWith("urn:uuid:");
    }
}
