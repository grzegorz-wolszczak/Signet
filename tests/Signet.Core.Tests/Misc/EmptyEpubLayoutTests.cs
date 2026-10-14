using System.IO;
using System.Linq;
using System.Xml.Linq;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>Tests of reading/writing the empty EPUB layout (<see cref="EmptyEpubLayout"/>).</summary>
public sealed class EmptyEpubLayoutTests
{
    private static readonly string[] BookPaths = { "OEBPS/content.opf", "OEBPS/Text/marker.xhtml", "OEBPS/toc.ncx" };

    [Fact]
    public void Write_then_Read_round_trips_the_book_paths()
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "layout.xml");

        EmptyEpubLayout.Write(path, BookPaths);

        EmptyEpubLayout.Read(path).Should().Equal(BookPaths);
    }

    [Fact]
    public void The_file_is_an_xml_list_of_book_paths()
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "layout.xml");

        EmptyEpubLayout.Write(path, BookPaths);

        XElement root = XDocument.Load(path).Root!;
        root.Name.LocalName.Should().Be("empty_epub_layout");
        root.Elements("book_path").Select(e => e.Value).Should().Equal(BookPaths);
    }

    [Fact]
    public void Read_returns_empty_for_a_missing_file()
    {
        EmptyEpubLayout.Read(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.xml"))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("<empty_epub_layout><book_path>")]
    [InlineData("{ \"bookpaths\": { \"empty_epub_bookpaths\": [ \"OEBPS/content.opf\" ] } }")]
    public void Read_returns_empty_for_a_malformed_or_json_file(string content)
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "bad.xml");
        File.WriteAllText(path, content);

        EmptyEpubLayout.Read(path).Should().BeEmpty();
    }

    [Fact]
    public void Default_file_is_signet_empty_epub_xml()
    {
        EmptyEpubLayout.DefaultLayoutFileName.Should().Be("signet_empty_epub.xml");
    }
}
