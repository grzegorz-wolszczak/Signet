using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="Book.CreateEmptyCssFile"/>/<see cref="Book.CreateEmptyJsFile"/>/
/// <see cref="Book.CreateEmptySvgFile"/>/<see cref="Book.AddExistingFiles"/>
/// (Book Browser: Add Blank / Add Existing Files).
/// </summary>
public sealed class BookAddResourceTests
{
    [Fact]
    public void CreateEmptyCssFile_AddsEmptyStylesheetToManifest()
    {
        using Book book = BookCreator.CreateNewBook("2.0");

        CssResource css = book.CreateEmptyCssFile();

        css.Filename.Should().Be("Style0001.css");
        css.MediaType.Should().Be("text/css");
        css.GetText().Should().BeEmpty();
        book.GetAllResources().Should().Contain(css);
        book.GetOpf().GetOpfDocument().Manifest.Should().Contain(i => i.Href.EndsWith(css.Filename));
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void CreateEmptyCssFile_SecondCall_GetsUniqueFilename()
    {
        using Book book = BookCreator.CreateNewBook("2.0");

        CssResource first = book.CreateEmptyCssFile();
        CssResource second = book.CreateEmptyCssFile();

        first.Filename.Should().NotBe(second.Filename);
    }

    [Fact]
    public void CreateEmptyJsFile_AddsScriptToManifest()
    {
        using Book book = BookCreator.CreateNewBook("3.0");

        MiscTextResource js = book.CreateEmptyJsFile();

        js.Filename.Should().Be("Script0001.js");
        js.MediaType.Should().Be("application/javascript");
        book.GetOpf().GetOpfDocument().Manifest.Should().Contain(i => i.Href.EndsWith(js.Filename));
    }

    [Fact]
    public void CreateEmptySvgFile_AddsSvgToManifest()
    {
        using Book book = BookCreator.CreateNewBook("3.0");

        SvgResource svg = book.CreateEmptySvgFile();

        svg.Filename.Should().Be("Image0001.svg");
        svg.MediaType.Should().Be("image/svg+xml");
        book.GetOpf().GetOpfDocument().Manifest.Should().Contain(i => i.Href.EndsWith(svg.Filename));
    }

    [Fact]
    public void AddExistingFiles_CopiesFilesAndDetectsType()
    {
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("2.0");

        string imagePath = temp.Combine("photo.jpg");
        File.WriteAllBytes(imagePath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        string cssPath = temp.Combine("extra.css");
        File.WriteAllText(cssPath, "body{}");

        var added = book.AddExistingFiles(new[] { imagePath, cssPath });

        added.Should().HaveCount(2);
        added[0].Should().BeOfType<ImageResource>();
        added[1].Should().BeOfType<CssResource>();
        book.GetAllResources().Select(r => r.Filename).Should().Contain("photo.jpg").And.Contain("extra.css");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void AddExistingFiles_DuplicateFilename_GetsUniqueName()
    {
        using TempDir temp = new();
        using Book book = BookCreator.CreateNewBook("2.0");
        string first = temp.Combine("dup.css");
        File.WriteAllText(first, "a{}");
        book.AddExistingFiles(new[] { first });

        string secondDir = temp.Combine("sub");
        Directory.CreateDirectory(secondDir);
        string second = Path.Combine(secondDir, "dup.css");
        File.WriteAllText(second, "b{}");
        var added = book.AddExistingFiles(new[] { second });

        added[0].Filename.Should().NotBe("dup.css");
    }
}
