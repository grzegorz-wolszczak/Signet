using System.IO;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests.TestSupport;

/// <summary>Checks that the tests see the corpus and that it has the expected structure.</summary>
public sealed class CorpusTests
{
    [Fact]
    public void Corpus_root_exists()
    {
        // Act & Assert
        Directory.Exists(CorpusPaths.Root).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ValidEpubDirs))]
    public void Valid_epub_has_container_and_mimetype(string dir)
    {
        // Act & Assert
        File.Exists(Path.Combine(dir, "mimetype")).Should().BeTrue("every valid fixture has a mimetype file");

        // Act & Assert
        File.ReadAllText(Path.Combine(dir, "mimetype")).Should().Be("application/epub+zip");

        // Act & Assert
        File.Exists(Path.Combine(dir, "META-INF", "container.xml")).Should().BeTrue();
    }

    [Fact]
    public void Media_fixture_contains_binary_stubs()
    {
        // Arrange
        string epub = Path.Combine(CorpusPaths.Epub3Media, "EPUB");

        // Act & Assert
        File.Exists(Path.Combine(epub, "images", "cover.png")).Should().BeTrue();

        // Act & Assert
        File.Exists(Path.Combine(epub, "fonts", "font.ttf")).Should().BeTrue();

        // Act & Assert
        File.Exists(Path.Combine(epub, "audio", "clip.mp3")).Should().BeTrue();

        // Act & Assert
        // >= 1040 B, so that IDPF obfuscation can be tested.
        new FileInfo(Path.Combine(epub, "fonts", "font.ttf")).Length.Should().BeGreaterThanOrEqualTo(1040);
    }

    [Fact]
    public void Malformed_fixtures_are_present()
    {
        // Act & Assert
        Directory.Exists(CorpusPaths.Malformed("missing-mimetype")).Should().BeTrue();

        // Act & Assert
        Directory.Exists(CorpusPaths.Malformed("no-rootfile")).Should().BeTrue();

        // Act & Assert
        Directory.Exists(CorpusPaths.Malformed("bad-opf-xml")).Should().BeTrue();

        // Act & Assert
        Directory.Exists(CorpusPaths.Malformed("not-wellformed-xhtml")).Should().BeTrue();

        // Act & Assert
        // "missing-mimetype" deliberately has no mimetype file.
        File.Exists(Path.Combine(CorpusPaths.Malformed("missing-mimetype"), "mimetype")).Should().BeFalse();
    }

    public static TheoryData<string> ValidEpubDirs()
    {
        TheoryData<string> data = new();
        foreach (string dir in CorpusPaths.ValidEpubDirs)
        {
            data.Add(dir);
        }

        return data;
    }
}
