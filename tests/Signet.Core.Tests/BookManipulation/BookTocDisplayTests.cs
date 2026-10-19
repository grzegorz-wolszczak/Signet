using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="Book.GetTocForDisplay"/> (the "Table Of Contents" panel): the tree built
/// from nav (EPUB 3) and from the NCX (EPUB 2), with resolved navigation targets.
/// </summary>
public sealed class BookTocDisplayTests
{
    private static Book Load(TempDir temp, string corpus) =>
        new ImportEpub(EpubBuilder.BuildInto(corpus, temp)).GetBook();

    [Fact]
    public void Uses_the_nav_document_for_an_epub3_book()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3WithNcx);

        // Act
        var entries = book.GetTocForDisplay();

        // Assert
        entries.Select(e => e.Title).Should().Equal("Chapter 1", "Chapter 2");
        entries[0].TargetBookPath.Should().Be("EPUB/text/chapter1.xhtml");
        entries[0].Fragment.Should().BeEmpty();
    }

    [Fact]
    public void Falls_back_to_the_ncx_for_an_epub2_book()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub2Minimal);

        // Act
        var entries = book.GetTocForDisplay();

        // Assert
        entries.Should().ContainSingle();
        entries[0].Title.Should().Be("Chapter 1");
        entries[0].TargetBookPath.Should().Be("OEBPS/Text/chapter1.xhtml");
    }

    [Fact]
    public void Resolves_a_fragment_from_the_nav_href()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Media);
        HtmlResource nav = book.GetNavResource()!;
        nav.SetText(nav.GetText().Replace("text/chapter1.xhtml\"", "text/chapter1.xhtml#part-2\""));

        // Act
        TocDisplayEntry entry = book.GetTocForDisplay().Single();

        // Assert
        entry.TargetBookPath.Should().Be("EPUB/text/chapter1.xhtml");
        entry.Fragment.Should().Be("part-2");
    }

    [Fact]
    public void Returns_an_empty_list_when_there_is_no_toc()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        HtmlResource nav = book.GetNavResource()!;
        nav.SetText(nav.GetText().Replace(
            "<li><a href=\"text/chapter1.xhtml\">Chapter 1</a></li>", string.Empty));

        // Act & Assert
        book.GetTocForDisplay().Should().BeEmpty();
    }
}
