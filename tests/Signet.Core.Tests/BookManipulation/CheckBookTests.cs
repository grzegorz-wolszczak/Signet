using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Messages;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// The Check Book additions (calibre CF-01): rule codes, skipped rules, the new checks (ids, images, fonts, CSS
/// declarations, a missing nav, an empty unique identifier) and the automatic fixes.
/// </summary>
public sealed class CheckBookTests
{
    // A 1x1 PNG (the corpus cover).
    private static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly string[] SkipInvalidId = { "Validation_InvalidId" };

    private static string Xhtml(string body, string head = "") =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head>\n<title>t</title>" + head + "\n</head>\n<body>\n"
        + body + "\n</body>\n</html>";

    private static (Book Book, HtmlResource Html) NewBook(string body, string head = "")
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource html = book.GetHtmlResourcesExcludingNav()[0];
        html.SetText(Xhtml(body, head));
        return (book, html);
    }

    // The rules of Signet's own validators; what EPUBCheck reports about the same book is tested in EpubCheckValidatorTests.
    private static List<ValidationResult> Check(Book book) =>
        BookValidator.ValidateCurrentBook(book).Where(r => !MessageIds.TryParse(r.Code, out _)).ToList();

    private static ValidationResult Single(Book book, string code) =>
        Check(book).Should().ContainSingle(r => r.Code == code).Subject;

    private static Resource AddFile(Book book, TempDir temp, string name, byte[] content)
    {
        string path = temp.Combine(name);
        File.WriteAllBytes(path, content);
        return book.AddExistingFiles(new[] { path })[0];
    }

    [Fact]
    public void Every_result_of_a_rule_of_signet_carries_the_resource_key_of_its_message_as_its_code()
    {
        // Arrange
        (Book book, _) = NewBook("<p id=\"1st\">a</p>");
        using (book)
        {
            // Act
            ValidationResult result = Single(book, "Validation_InvalidId");

            // Assert
            result.Message.Should().Contain("1st");
            result.Fix.Should().BeOfType<InvalidIdFix>();
        }
    }

    [Fact]
    public void Skipped_rules_are_left_out()
    {
        // Arrange
        (Book book, _) = NewBook("<p id=\"1st\">a</p>");
        using (book)
        {
            // Act & Assert
            Check(book).Should().Contain(r => r.Code == "Validation_InvalidId");

            // Act & Assert
            BookValidator.ValidateCurrentBook(book, SkipInvalidId).Should().NotContain(r => r.Code == "Validation_InvalidId");
        }
    }

    [Fact]
    public void An_invalid_id_is_renamed_in_the_file_its_links_and_its_css_selectors()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<p id=\"1st\">a</p>\n<p><a href=\"#1st\">same file</a></p>",
            "<style>#\\31 st { color: red; } p { color: #1st; }</style>");
        using (book)
        {
            HtmlResource other = book.CreateEmptyHtmlFile();
            string link = Core.BookPath.Relative(other.BookPath, html.BookPath) + "#1st";
            other.SetText(Xhtml($"<p><a href=\"{link}\">other file</a></p>"));

            // Act
            Single(book, "Validation_InvalidId").Fix!.Apply(book).Should().BeTrue();

            // Assert
            string text = html.GetText();
            text.Should().Contain("<p id=\"id_1st\">").And.Contain("href=\"#id_1st\"")
                .And.Contain("#id_1st { color: red; }")
                .And.Contain("p { color: #1st; }", "a value inside a declaration block is not a selector");
            other.GetText().Should().Contain(Core.BookPath.Relative(other.BookPath, html.BookPath) + "#id_1st");
            Check(book).Should().NotContain(r => r.Code == "Validation_InvalidId" || r.Code.StartsWith("Validation_BadFragment", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void An_invalid_id_is_renamed_in_the_aria_references_and_attribute_selectors_that_refer_to_it()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook(
            "<h2 id=\"1st\">t</h2>\n<p aria-labelledby=\"1st other\" aria-describedby=\"1stx\">a</p><label for=\"1st\">l</label>",
            "<style>[id=\"1st\"] { color: red; } /* #1st [id=\"1st\"] */</style>");
        using (book)
        {
            // Act
            Single(book, "Validation_InvalidId").Fix!.Apply(book).Should().BeTrue();

            // Assert
            string text = html.GetText();
            text.Should().Contain("<h2 id=\"id_1st\">")
                .And.Contain("aria-labelledby=\"id_1st other\"")
                .And.Contain("aria-describedby=\"1stx\"", "another id")
                .And.Contain("<label for=\"id_1st\">")
                .And.Contain("[id=\"id_1st\"] { color: red; }")
                .And.Contain("/* #1st [id=\"1st\"] */", "a comment is not a selector");
        }
    }

    [Fact]
    public void Bare_text_in_the_body_is_wrapped_in_paragraphs()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("Loose text\n<p>kept <b>as</b> is</p>\n tail ");
        using (book)
        {
            // Act
            Single(book, "Validation_BareBodyText").Fix!.Apply(book).Should().BeTrue();

            // Assert
            html.GetText().Should().Contain("<body>\n<p>Loose text</p>\n<p>kept <b>as</b> is</p>\n <p>tail</p> \n</body>");
            Check(book).Should().NotContain(r => r.Code == "Validation_BareBodyText");
        }
    }

    [Fact]
    public void A_manifest_media_type_that_does_not_match_the_extension_is_corrected()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        CssResource css = book.CreateEmptyCssFile();
        OpfDocument document = book.GetOpf().GetOpfDocument();
        document.Manifest.Single(m => m.Href.EndsWith(css.Filename, StringComparison.Ordinal)).MediaType = "text/plain";
        book.GetOpf().SetOpfDocument(document);

        // Act
        Single(book, "Validation_OpfMediaTypeMismatch").Fix!.Apply(book).Should().BeTrue();

        // Assert
        book.GetOpf().GetOpfDocument().Manifest.Single(m => m.Href.EndsWith(css.Filename, StringComparison.Ordinal))
            .MediaType.Should().Be("text/css");
    }

    [Fact]
    public void A_truncated_image_is_reported_as_damaged()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("3.0");
        AddFile(book, temp, "whole.png", Png1x1);
        AddFile(book, temp, "cut.png", Png1x1[..^12]);

        // Act & Assert
        Single(book, "Validation_ImageCorrupt").BookPath.Should().EndWith("cut.png");
    }

    [Fact]
    public void A_cmyk_jpeg_is_reported()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("3.0");
        AddFile(book, temp, "rgb.jpg", Jpeg(components: 3));
        AddFile(book, temp, "cmyk.jpg", Jpeg(components: 4));

        // Act & Assert
        Single(book, "Validation_ImageCmyk").BookPath.Should().EndWith("cmyk.jpg");

        // Act & Assert
        Check(book).Should().NotContain(r => r.Code == "Validation_ImageCorrupt");
    }

    [Theory]
    [InlineData(0x774F4646, 0, true)]
    [InlineData(0x774F4646, 4, false)]
    [InlineData(0x774F4632, 0, true)]
    [InlineData(0x12345678, 0, false)]
    public void A_web_font_must_be_as_long_as_its_header_says(uint signature, int missing, bool intact)
    {
        // Arrange
        byte[] font = new byte[64];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(font, signature);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(8), (uint)font.Length);

        // Act & Assert
        FontIntegrityValidator.IsIntact(font[..^missing]).Should().Be(intact);
    }

    [Fact]
    public void Unknown_css_properties_and_empty_values_are_reported_with_their_line()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        CssResource css = book.CreateEmptyCssFile();
        css.SetText("p {\r\n  color: red;\r\n}\r\n\r\nh1 {\r\n  colr: red;\r\n  margin: ;\r\n  padding: !important;\r\n"
            + "  -webkit-hyphens: auto;\r\n  --accent: blue;\r\n  font-weight: bold !important;\r\n}\r\n");

        // Act
        List<ValidationResult> results = Check(book).Where(r => r.BookPath == css.BookPath).ToList();

        // Assert
        results.Select(r => (r.Code, r.Line)).Should().BeEquivalentTo(new[]
        {
            ("Validation_CssUnknownProperty", 5), ("Validation_CssEmptyValue", 5), ("Validation_CssEmptyValue", 5),
        });
        results[0].Message.Should().Contain("colr").And.Contain("h1");
    }

    [Fact]
    public void Css_in_style_elements_is_checked_too()
    {
        // Arrange
        (Book book, HtmlResource html) = NewBook("<p>a</p>", "\n<style>\np { colour: red; }\n</style>");
        using (book)
        {
            // Act
            ValidationResult result = Single(book, "Validation_CssUnknownProperty");

            // Assert
            result.BookPath.Should().Be(html.BookPath);
            result.Line.Should().Be(7);
        }
    }

    // A minimal baseline JPEG header: SOI, a 1x1 SOF0 with the given number of components, EOI.
    private static byte[] Jpeg(int components)
    {
        List<byte> data = new() { 0xFF, 0xD8, 0xFF, 0xC0 };
        int length = 8 + (3 * components);
        data.AddRange(new[] { (byte)(length >> 8), (byte)length, (byte)8, (byte)0, (byte)1, (byte)0, (byte)1, (byte)components });
        for (int i = 1; i <= components; i++)
        {
            data.AddRange(new[] { (byte)i, (byte)0x11, (byte)0 });
        }

        data.AddRange(new byte[] { 0xFF, 0xD9 });
        return data.ToArray();
    }
}
