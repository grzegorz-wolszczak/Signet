using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="Book.GetNavResource"/>/<see cref="Book.IsNavInSpine"/>/
/// <see cref="Book.FindExistingCoverHtmlResource"/>/<see cref="Book.CreateHtmlCoverFile"/>/
/// <see cref="Book.SetCoverImage"/> (semantics, Guide, Landmarks, Cover, Nav in spine).
/// </summary>
public sealed class BookCoverTests
{
    // 1x1 transparent PNG.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static ImageResource AddPngImage(Book book, TempDir temp, string filename)
    {
        string path = temp.Combine(filename);
        File.WriteAllBytes(path, OnePixelPng);
        return (ImageResource)book.AddExistingFiles(new[] { path }).Single();
    }

    [Fact]
    public void GetNavResource_Epub2_ReturnsNull()
    {
        using Book book = BookCreator.CreateNewBook("2.0");

        book.GetNavResource().Should().BeNull();
        book.IsNavInSpine.Should().BeFalse();
    }

    [Fact]
    public void GetNavResource_Epub3_ResolvesNavFromManifest()
    {
        using Book book = BookCreator.CreateNewBook("3.0");

        HtmlResource? nav = book.GetNavResource();

        nav.Should().NotBeNull();
        nav!.Filename.Should().Be("nav.xhtml");
    }

    [Fact]
    public void IsNavInSpine_DefaultNewBook_IsInSpine()
    {
        // BookCreator adds nav.xhtml like any other HTML file — it lands in the spine automatically
        // (AppendManifestEntry). "Nav in spine" is therefore the default state of a new book.
        using Book book = BookCreator.CreateNewBook("3.0");

        book.IsNavInSpine.Should().BeTrue();
    }

    [Fact]
    public void IsNavInSpine_RemoveThenAddNav_TogglesCorrectly()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource nav = book.GetNavResource()!;

        book.GetOpf().RemoveResourceFromSpine(nav);
        book.IsNavInSpine.Should().BeFalse();

        book.GetOpf().AppendResourceToSpine(nav, nonlinear: true);
        book.IsNavInSpine.Should().BeTrue();
    }

    [Fact]
    public void FindExistingCoverHtmlResource_NoneMarked_ReturnsNull()
    {
        using Book book = BookCreator.CreateNewBook("2.0");

        book.FindExistingCoverHtmlResource().Should().BeNull();
    }

    [Fact]
    public void FindExistingCoverHtmlResource_ByFilename_IsFound()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource cover = book.CreateEmptyHtmlFile();
        cover.RenameTo("cover.xhtml");

        book.FindExistingCoverHtmlResource().Should().Be(cover);
    }

    [Fact]
    public void CreateHtmlCoverFile_PlacesNewFileFirstInSpine()
    {
        using Book book = BookCreator.CreateNewBook("2.0");

        HtmlResource cover = book.CreateHtmlCoverFile();

        cover.Filename.Should().Be("cover.xhtml");
        book.GetHtmlResources().First().Should().Be(cover);
        book.GetOpf().GetReadingOrder(cover).Should().Be(0);
    }

    [Fact]
    public void CreateHtmlCoverFile_Epub3_NavNotInSpine_StaysExcludedFromSpine()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource nav = book.GetNavResource()!;
        book.GetOpf().RemoveResourceFromSpine(nav);
        book.IsNavInSpine.Should().BeFalse();

        book.CreateHtmlCoverFile();

        book.IsNavInSpine.Should().BeFalse();
        book.GetOpf().GetReadingOrder(nav).Should().Be(-1);
    }

    [Fact]
    public void CreateHtmlCoverFile_Epub3_NavInSpine_StaysIncludedAfterCover()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource nav = book.GetNavResource()!;
        book.IsNavInSpine.Should().BeTrue();

        HtmlResource cover = book.CreateHtmlCoverFile();

        book.IsNavInSpine.Should().BeTrue();
        book.GetHtmlResources().First().Should().Be(cover);
    }

    [Fact]
    public void SetCoverImage_Epub2_SetsGuideSemanticsAndCoverMeta()
    {
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("2.0");
        ImageResource image = AddPngImage(book, temp, "figure.png");

        HtmlResource cover = book.SetCoverImage(image);

        book.GetOpf().GetGuideSemanticCodeForResource(cover).Should().Be("cover");
        book.GetOpf().IsCoverImage(image).Should().BeTrue();
        book.FindExistingCoverHtmlResource().Should().Be(cover);
    }

    [Fact]
    public void SetCoverImage_Epub2_SubstitutesImagePlaceholders()
    {
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("2.0");
        ImageResource image = AddPngImage(book, temp, "figure.png");

        HtmlResource cover = book.SetCoverImage(image);

        string text = cover.GetText();
        text.Should().NotContain("SGC_IMAGE_FILENAME").And.NotContain("SGC_IMAGE_WIDTH").And.NotContain("SGC_IMAGE_HEIGHT");
        text.Should().Contain("width=\"1\"").And.Contain("height=\"1\"");
        text.Should().Contain(image.Filename);
    }

    [Fact]
    public void SetCoverImage_Epub3_SetsLandmarkAndManifestProperties()
    {
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("3.0");
        ImageResource image = AddPngImage(book, temp, "figure.png");

        HtmlResource cover = book.SetCoverImage(image);

        HtmlResource nav = book.GetNavResource()!;
        new NavProcessor(nav).GetLandmarkCodeForResource(cover).Should().Be("cover");
        book.GetOpf().IsCoverImage(image).Should().BeTrue();

        var manifestEntry = book.GetOpf().GetOpfDocument().Manifest.First(
            m => m.Href.EndsWith(cover.Filename, StringComparison.Ordinal));
        manifestEntry.Attributes.Value("properties").Should().Contain("svg");
    }

    [Fact]
    public void SetCoverImage_ExistingCoverProvided_ReusesResourceInsteadOfCreatingNew()
    {
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource existingCover = book.CreateEmptyHtmlFile();
        existingCover.RenameTo("cover.xhtml");
        ImageResource image = AddPngImage(book, temp, "figure.png");

        HtmlResource cover = book.SetCoverImage(image, existingCover);

        cover.Should().Be(existingCover);
        book.GetHtmlResources().Count(r => string.Equals(r.Filename, "cover.xhtml", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }
}
