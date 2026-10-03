using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of the <see cref="Book"/> API backing the "Insert" menu: the list of media resources
/// and the identifiers in (X)HTML files.
/// </summary>
public sealed class BookInsertSupportTests
{
    private static Book Load(TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp)).GetBook();

    [Fact]
    public void GetMediaResources_lists_the_book_images()
    {
        using TempDir temp = new();
        using Book book = Load(temp);

        book.GetMediaResources().OfType<ImageResource>().Select(r => r.Filename)
            .Should().Contain("figure.png");
    }

    [Fact]
    public void GetIdsInHtmlFile_returns_the_ids_present_in_the_document()
    {
        using TempDir temp = new();
        using Book book = Load(temp);
        HtmlResource chapter = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");
        chapter.SetText(chapter.GetText().Replace("<body>", "<body>\n<p id=\"para-one\">hi</p>"));

        book.GetIdsInHtmlFile(chapter).Should().Contain("para-one");
        book.GetIdsInHtmlFiles()[chapter.BookPath].Should().Contain("para-one");
    }
}
