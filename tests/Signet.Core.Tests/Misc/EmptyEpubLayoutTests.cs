using System.IO;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Misc;

/// <summary>Tests of reading/writing the empty EPUB layout (<see cref="EmptyEpubLayout"/>).</summary>
public sealed class EmptyEpubLayoutTests
{
    [Fact]
    public void Write_then_Read_round_trips_the_book_paths()
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "layout.json");
        string[] bookPaths = { "OEBPS/content.opf", "OEBPS/Text/marker.xhtml", "OEBPS/toc.ncx" };

        EmptyEpubLayout.Write(path, bookPaths);

        EmptyEpubLayout.Read(path).Should().Equal(bookPaths);
    }

    [Fact]
    public void Read_returns_empty_for_a_missing_file()
    {
        EmptyEpubLayout.Read(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.json"))
            .Should().BeEmpty();
    }

    [Fact]
    public void Read_returns_empty_for_malformed_json()
    {
        using TempDir dir = new();
        string path = Path.Combine(dir.Path, "bad.json");
        File.WriteAllText(path, "{ not json");

        EmptyEpubLayout.Read(path).Should().BeEmpty();
    }
}
