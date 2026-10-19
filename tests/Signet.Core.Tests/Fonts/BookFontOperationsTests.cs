using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Fonts;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Fonts;

/// <summary>
/// The font operations on a real <see cref="Book"/>: <see cref="Book.AddFontFaces"/>, <see cref="FontManagerReport"/>
/// and applying a <see cref="FontRemovalPlan"/> with <see cref="Book.ApplyTexts"/>.
/// </summary>
public sealed class BookFontOperationsTests
{
    private readonly Fixture _fixture = new();

    private (Book Book, IReadOnlyList<FontResource> Fonts) NewBookWithFonts(TempDir temp, string html)
    {
        Book book = BookCreator.CreateNewBook("3.0");
        book.GetHtmlResources()[0].SetText(html);
        string family = "Fam" + _fixture.Create<int>();
        string regular = temp.Combine("Fam-Regular.ttf");
        string bold = temp.Combine("Fam-Bold.ttf");
        File.WriteAllBytes(regular, FontFileBuilder.Ttf(family, "Regular", 400, false));
        File.WriteAllBytes(bold, FontFileBuilder.Ttf(family, "Bold", 700, false));
        IReadOnlyList<FontResource> fonts = book.AddExistingFiles(new[] { regular, bold }).Cast<FontResource>().ToList();
        return (book, fonts);
    }

    private static string Html(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head>\n<title>t</title>\n</head>\n<body>\n"
        + body + "\n</body>\n</html>\n";

    [Fact]
    public void AddFontFaces_without_a_stylesheet_creates_fonts_css_and_links_it()
    {
        // Arrange
        using TempDir temp = new();
        (Book book, IReadOnlyList<FontResource> fonts) = NewBookWithFonts(temp, Html("<p>text</p>"));
        using (book)
        {
            FontFaceCandidate candidate = FontFaceAddition.Describe(fonts[0], book.IsEpub3);
            FontFaceRequest request = new(fonts[0].BookPath, candidate.Suggested.Family, candidate.Suggested.Weight, candidate.Suggested.Style);

            // Act
            (CssResource css, IReadOnlyList<HtmlResource> notLinked) = book.AddFontFaces(null, new[] { request }, candidate.Suggested.Family);

            // Assert
            css.Filename.Should().Be("fonts.css");
            notLinked.Should().BeEmpty();
            book.GetHtmlResources()[0].GetLinkedStylesheets().Should().Equal(css.BookPath);
            css.GetText().Should().Contain("src: url(\"../Fonts/Fam-Regular.ttf\");").And.Contain("body {\n  font-family: ");
        }
    }

    [Fact]
    public void FontManagerReport_lists_files_families_and_missing_faces()
    {
        // Arrange
        using TempDir temp = new();
        (Book book, IReadOnlyList<FontResource> fonts) = NewBookWithFonts(temp, Html("<p>text <i>slanted</i></p>"));
        using (book)
        {
            string family = fonts[0].GetFontInfo().Family;

            // Act
            book.AddFontFaces(null, new[] { new FontFaceRequest(fonts[0].BookPath, family, "normal", "normal") }, family);

            FontManagerReport report = FontManagerReport.Build(book);

            // Assert
            report.Files.Select(f => f.IsUnused).Should().Equal(false, true);
            FontFamilyReport familyReport = report.Families.Should().ContainSingle().Subject;
            familyReport.IsEmbedded.Should().BeTrue();
            familyReport.Usages.Should().ContainSingle();
            familyReport.Elements.Should().Be(2);
            familyReport.Missing.Should().ContainSingle().Which.Variant.Should().Be("italic");
        }
    }

    [Fact]
    public void A_removal_plan_applied_to_the_book_cleans_the_stylesheet()
    {
        // Arrange
        using TempDir temp = new();
        (Book book, IReadOnlyList<FontResource> fonts) = NewBookWithFonts(temp, Html("<p>text</p>"));
        using (book)
        {
            string family = fonts[0].GetFontInfo().Family;
            (CssResource css, _) = book.AddFontFaces(
                null,
                fonts.Select(f => new FontFaceRequest(f.BookPath, family, f == fonts[0] ? "normal" : "bold", "normal")).ToList(),
                family);

            // Act
            FontRemovalPlan plan = FontRemovalPlan.Create(BookFontIndex.Build(book), fonts.Select(f => f.BookPath).ToList());
            book.ApplyTexts(plan.BuildNewTexts(new HashSet<string>())).Should().BeTrue();

            // Assert
            css.GetText().Should().Be("body {\n  font-family: serif;\n}\n");
        }
    }
}
