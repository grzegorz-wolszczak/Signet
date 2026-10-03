using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of <see cref="OpfModel.RenameResource"/> — a full rename with an update of
/// all references (href/src/url) in the publication.
/// </summary>
public sealed class OpfModelRenameTests
{
    private static Book Load(string corpusDir, TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(corpusDir, temp)).GetBook();

    [Fact]
    public void RenameResource_ImageReferencedFromHtml_UpdatesImgSrc()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        HtmlResource chapter = book.GetHtmlResources().Single(r => r.Filename == "chapter1.xhtml");

        bool result = model.RenameResource(image, "figure-renamed.png", out string? error);

        result.Should().BeTrue();
        error.Should().BeNull();
        image.Filename.Should().Be("figure-renamed.png");
        chapter.GetText().Should().Contain("src=\"../images/figure-renamed.png\"");
        chapter.GetText().Should().NotContain("figure.png\"");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void RenameResource_CssReferencedFromHtml_UpdatesLinkHref()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource css = book.GetAllResources().Single(r => r.Filename == "style.css");
        HtmlResource chapter = book.GetHtmlResources().Single(r => r.Filename == "chapter1.xhtml");

        bool result = model.RenameResource(css, "style-renamed.css", out string? error);

        result.Should().BeTrue();
        chapter.GetText().Should().Contain("href=\"../styles/style-renamed.css\"");
    }

    [Fact]
    public void RenameResource_FontReferencedFromCss_UpdatesFontFaceUrl()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource font = book.GetAllResources().Single(r => r.Filename == "font.ttf");
        CssResource style = (CssResource)book.GetAllResources().Single(r => r.Filename == "style.css");

        bool result = model.RenameResource(font, "font-renamed.ttf", out string? error);

        result.Should().BeTrue();
        style.GetText().Should().Contain("url(\"../fonts/font-renamed.ttf\")");
    }

    [Fact]
    public void RenameResource_ForbiddenCharacter_FailsWithoutModifyingBook()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");

        bool result = model.RenameResource(image, "bad:name.png", out string? error);

        result.Should().BeFalse();
        error.Should().NotBeNull();
        image.Filename.Should().Be("figure.png");
        book.Modified.Should().BeFalse();
    }

    [Fact]
    public void RenameResource_NameAlreadyInUse_Fails()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");

        bool result = model.RenameResource(image, "cover.png", out string? error);

        result.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public void RenameResource_SameName_IsNoOpAndSucceeds()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");

        bool result = model.RenameResource(image, "figure.png", out string? error);

        result.Should().BeTrue();
        error.Should().BeNull();
        book.Modified.Should().BeFalse();
    }

    [Fact]
    public void RenameResource_WithoutExtension_KeepsOriginalExtension()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");

        bool result = model.RenameResource(image, "figure-renamed", out string? error);

        result.Should().BeTrue();
        image.Filename.Should().Be("figure-renamed.png");
    }
}
