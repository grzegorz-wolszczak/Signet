using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="BookCreator"/> — creating a new, empty publication.</summary>
public sealed class BookCreatorTests
{
    [Theory]
    [InlineData("2.0")]
    [InlineData("3.0")]
    public void CreateNewBook_produces_a_minimal_standard_publication(string version)
    {
        using Book book = BookCreator.CreateNewBook(version);

        book.EpubVersion.Should().Be(version);
        book.Modified.Should().BeFalse();

        book.GetHtmlResources().Should().ContainSingle(r => r.Filename == "Section0001.xhtml");
        book.GetOpf().GetSpineOrderBookPaths().Should().Contain(p => p.EndsWith("Section0001.xhtml"));
    }

    [Fact]
    public void CreateNewBook_epub2_has_an_ncx_on_the_spine()
    {
        using Book book = BookCreator.CreateNewBook("2.0");

        book.GetNcx().Should().NotBeNull();
        book.GetOpf().GetOpfDocument().SpineAttributes.Attributes.Value("toc").Should().NotBeEmpty();
    }

    [Fact]
    public void CreateNewBook_epub3_has_a_nav_document_and_no_ncx()
    {
        using Book book = BookCreator.CreateNewBook("3.0");

        book.GetNcx().Should().BeNull();
        book.GetOpf().GetNavResourceBookPath().Should().EndWith("nav.xhtml");
    }

    [Fact]
    public void CreateNewBook_empty_version_defaults_to_epub2()
    {
        using Book book = BookCreator.CreateNewBook(null);
        book.EpubVersion.Should().Be("2.0");
    }

    [Fact]
    public void CreateNewBook_falls_back_to_standard_layout_for_an_incomplete_custom_layout()
    {
        string[] incomplete = { "OEBPS/content.opf" };
        using Book book = BookCreator.CreateNewBook("2.0", incomplete);

        book.GetHtmlResources().Should().NotBeEmpty();
        book.GetNcx().Should().NotBeNull();
    }

    [Fact]
    public void CreateNewBook_honours_a_valid_custom_layout()
    {
        string[] layout =
        {
            "content.opf", "text/marker.xhtml", "css/marker.css",
            "img/marker.jpg", "toc.ncx",
        };

        using Book book = BookCreator.CreateNewBook("2.0", layout);

        book.GetHtmlResources().Should().ContainSingle()
            .Which.BookPath.Should().Be("text/Section0001.xhtml");
    }

    [Fact]
    public void A_new_book_round_trips_through_export_and_import()
    {
        using TempDir temp = new();
        string path = Path.Combine(temp.Path, "new.epub");

        using (Book book = BookCreator.CreateNewBook("3.0"))
        {
            new ExportEpub(book).WriteBook(path);
        }

        using Book reopened = new ImportEpub(path).GetBook();
        reopened.IsEpub3.Should().BeTrue();
        reopened.GetHtmlResources().Select(r => r.Filename).Should().Contain("Section0001.xhtml");
        reopened.GetOpf().GetNavResourceBookPath().Should().EndWith("nav.xhtml");
    }
}
