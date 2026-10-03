using System.IO;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Tests of <see cref="EpubComparer"/> itself — they confirm that it detects real differences and ignores
/// insignificant ones (whitespace, attribute order, volatile meta, designated entries).
/// </summary>
public sealed class EpubComparerTests
{
    [Fact]
    public void Identical_archives_are_equivalent()
    {
        byte[] epub = EpubBuilder.BuildBytes(CorpusPaths.Epub3Minimal);

        EpubComparisonResult result = EpubComparer.CompareBytes(epub, epub);

        result.AreEquivalent.Should().BeTrue(result.Report);
    }

    [Theory]
    [MemberData(nameof(ValidEpubDirs))]
    public void Every_valid_fixture_equals_a_fresh_build_of_itself(string dir)
    {
        EpubComparisonResult result = EpubComparer.CompareBytes(
            EpubBuilder.BuildBytes(dir),
            EpubBuilder.BuildBytes(dir));

        result.AreEquivalent.Should().BeTrue(result.Report);
    }

    [Fact]
    public void Changed_text_content_is_detected()
    {
        (byte[] a, byte[] b) = BuildPair(CorpusPaths.Epub3Minimal, mutate: dir =>
        {
            string file = Path.Combine(dir, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(file, File.ReadAllText(file).Replace("Hello, world.", "Goodbye, world."));
        });

        EpubComparer.CompareBytes(a, b).Differences.Should().ContainSingle()
            .Which.Should().Contain("chapter1.xhtml");
    }

    [Fact]
    public void Added_file_is_reported_as_only_in_B()
    {
        (byte[] a, byte[] b) = BuildPair(CorpusPaths.Epub3Minimal, mutate: dir =>
            File.WriteAllText(Path.Combine(dir, "EPUB", "extra.txt"), "extra"));

        EpubComparer.CompareBytes(a, b).Differences.Should().ContainSingle()
            .Which.Should().Contain("tylko w B").And.Contain("extra.txt");
    }

    [Fact]
    public void Removed_file_is_reported_as_only_in_A()
    {
        (byte[] a, byte[] b) = BuildPair(CorpusPaths.Epub3Minimal, mutate: dir =>
            File.Delete(Path.Combine(dir, "EPUB", "styles", "style.css")));

        EpubComparer.CompareBytes(a, b).Differences.Should().ContainSingle()
            .Which.Should().Contain("tylko w A").And.Contain("style.css");
    }

    [Fact]
    public void Indentation_only_difference_in_xml_is_ignored()
    {
        const string reindented =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"pub-id\" xml:lang=\"en\">\n" +
            "        <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n" +
            "                <dc:identifier id=\"pub-id\">urn:uuid:11111111-1111-4111-8111-111111111111</dc:identifier>\n" +
            "                <dc:title>Minimal EPUB 3</dc:title>\n" +
            "                <dc:language>en</dc:language>\n" +
            "                <meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta>\n" +
            "        </metadata>\n" +
            "        <manifest>\n" +
            "                <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n" +
            "                <item id=\"css\" href=\"styles/style.css\" media-type=\"text/css\"/>\n" +
            "                <item id=\"ch1\" href=\"text/chapter1.xhtml\" media-type=\"application/xhtml+xml\"/>\n" +
            "        </manifest>\n" +
            "        <spine><itemref idref=\"ch1\"/></spine>\n" +
            "</package>\n";

        (byte[] a, byte[] b) = BuildPair(CorpusPaths.Epub3Minimal, mutate: dir =>
            File.WriteAllText(Path.Combine(dir, "EPUB", "package.opf"), reindented));

        EpubComparer.CompareBytes(a, b).AreEquivalent.Should().BeTrue();
    }

    [Fact]
    public void Attribute_order_difference_is_ignored()
    {
        (byte[] a, byte[] b) = BuildPair(CorpusPaths.Epub3Minimal, mutate: dir =>
        {
            string file = Path.Combine(dir, "EPUB", "package.opf");
            File.WriteAllText(file, File.ReadAllText(file).Replace(
                "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>",
                "<item properties=\"nav\" media-type=\"application/xhtml+xml\" href=\"nav.xhtml\" id=\"nav\"/>"));
        });

        EpubComparer.CompareBytes(a, b).AreEquivalent.Should().BeTrue();
    }

    [Fact]
    public void Volatile_modified_meta_differs_under_default_but_not_under_roundtrip()
    {
        (byte[] a, byte[] b) = BuildPair(CorpusPaths.Epub3Minimal, mutate: dir =>
        {
            string file = Path.Combine(dir, "EPUB", "package.opf");
            File.WriteAllText(file, File.ReadAllText(file).Replace(
                "2026-01-01T00:00:00Z", "2099-12-31T23:59:59Z"));
        });

        EpubComparer.CompareBytes(a, b, EpubComparisonOptions.Default).AreEquivalent.Should().BeFalse();
        EpubComparer.CompareBytes(a, b, EpubComparisonOptions.RoundTrip).AreEquivalent.Should().BeTrue();
    }

    [Fact]
    public void Ignored_entry_is_skipped()
    {
        (byte[] a, byte[] b) = BuildPair(CorpusPaths.Epub3Minimal, mutate: dir =>
        {
            string file = Path.Combine(dir, "EPUB", "styles", "style.css");
            File.WriteAllText(file, "body { color: red; }\n");
        });

        EpubComparisonResult withDefault = EpubComparer.CompareBytes(a, b);
        withDefault.AreEquivalent.Should().BeFalse();

        EpubComparisonOptions options = EpubComparisonOptions.Default;
        options.IgnoredEntries.Add("EPUB/styles/style.css");

        EpubComparer.CompareBytes(a, b, options).AreEquivalent.Should().BeTrue();
    }

    [Fact]
    public void Binary_difference_is_detected()
    {
        (byte[] a, byte[] b) = BuildPair(CorpusPaths.Epub3Media, mutate: dir =>
        {
            string images = Path.Combine(dir, "EPUB", "images");
            // Replace cover.png with the (smaller) content of figure.png.
            File.Copy(Path.Combine(images, "figure.png"), Path.Combine(images, "cover.png"), overwrite: true);
        });

        EpubComparer.CompareBytes(a, b).Differences.Should().ContainSingle()
            .Which.Should().Contain("cover.png").And.Contain("Bajty");
    }

    private static (byte[] A, byte[] B) BuildPair(string fixtureDir, System.Action<string> mutate)
    {
        TempDir temp = new();
        try
        {
            string a = TestFs.CopyDirectory(fixtureDir, temp.Combine("a"));
            string b = TestFs.CopyDirectory(fixtureDir, temp.Combine("b"));
            mutate(b);
            return (EpubBuilder.BuildBytes(a), EpubBuilder.BuildBytes(b));
        }
        finally
        {
            temp.Dispose();
        }
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
