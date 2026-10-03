using System;
using System.IO;
using System.Text;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>
/// Tests of the stateless helpers of <see cref="Utility"/>.
/// </summary>
public sealed class UtilityTests
{
    [Theory]
    [InlineData("&lt;p&gt;A &amp; B&lt;/p&gt;", "<p>A & B</p>")]
    [InlineData("Tom &apos;Cat&apos; &quot;Jr&quot;", "Tom 'Cat' \"Jr\"")]
    [InlineData("no entities here", "no entities here")]
    [InlineData("&amp;amp;", "&amp;")]
    public void DecodeXml_expands_the_five_predefined_entities(string input, string expected)
    {
        Utility.DecodeXml(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("A & B", "A &amp; B")]
    [InlineData("<p>\"x\"</p>", "&lt;p&gt;&quot;x&quot;&lt;/p&gt;")]
    [InlineData("a > b", "a &gt; b")]
    public void EncodeXml_escapes_xml_metacharacters(string input, string expected)
    {
        Utility.EncodeXml(input).Should().Be(expected);
    }

    [Fact]
    public void EncodeXml_does_not_double_encode_existing_entities()
    {
        Utility.EncodeXml("already &amp; encoded").Should().Be("already &amp; encoded");
    }

    [Fact]
    public void EncodeXml_leaves_the_apostrophe_untouched_like_qt()
    {
        Utility.EncodeXml("it's fine").Should().Be("it's fine");
    }

    [Fact]
    public void CreateUuid_returns_a_braceless_lowercase_uuid()
    {
        string uuid = Utility.CreateUuid();

        uuid.Should().NotContain("{").And.NotContain("}");
        uuid.Should().Be(uuid.ToLowerInvariant());
        Guid.TryParse(uuid, out _).Should().BeTrue();
        Utility.CreateUuid().Should().NotBe(uuid);
    }

    [Fact]
    public void GetTemporaryFileName_builds_a_unique_path_without_creating_a_file()
    {
        string path = Utility.GetTemporaryFileName(".xhtml");

        path.Should().StartWith(Path.GetTempPath());
        Path.GetFileName(path).Should().StartWith(Utility.TemporaryFilePrefix).And.EndWith(".xhtml");
        File.Exists(path).Should().BeFalse();
        Utility.GetTemporaryFileName(".xhtml").Should().NotBe(path);
    }

    [Fact]
    public void GetTemporaryFileName_accepts_an_empty_extension()
    {
        string path = Utility.GetTemporaryFileName(string.Empty);

        Path.GetFileName(path).Should().StartWith(Utility.TemporaryFilePrefix);
        Path.GetExtension(path).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "da39a3ee5e6b4b0d3255bfef95601890afd80709")]
    [InlineData("abc", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    public void Sha1Hex_matches_known_vectors(string text, string expected)
    {
        Utility.Sha1Hex(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
    public void Md5Hex_matches_known_vectors(string text, string expected)
    {
        Utility.Md5Hex(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Sha256Hex_matches_known_vectors(string text, string expected)
    {
        Utility.Sha256Hex(text).Should().Be(expected);
    }

    [Fact]
    public void Hash_overloads_agree_across_bytes_stream_and_text()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Signet");
        using MemoryStream stream = new(bytes);

        string fromText = Utility.Sha1Hex("Signet");
        string fromBytes = Utility.Sha1Hex(bytes);
        string fromStream = Utility.Sha1Hex(stream);

        fromBytes.Should().Be(fromText);
        fromStream.Should().Be(fromText);
    }

    [Theory]
    [AutoData]
    public void Hashes_are_deterministic_and_correctly_sized(byte[] data)
    {
        Utility.Sha1Hex(data).Should().Be(Utility.Sha1Hex(data)).And.HaveLength(40);
        Utility.Md5Hex(data).Should().Be(Utility.Md5Hex(data)).And.HaveLength(32);
        Utility.Sha256Hex(data).Should().Be(Utility.Sha256Hex(data)).And.HaveLength(64);
    }

    [Fact]
    public void SortByCounts_orders_folders_by_descending_count_stably()
    {
        string[] folders = { "OEBPS/A", "OEBPS/B", "OEBPS/C" };
        int[] counts = { 2, 5, 2 };

        Utility.SortByCounts(folders, counts).Should().Equal("OEBPS/B", "OEBPS/A", "OEBPS/C");
    }

    [Fact]
    public void SortByCounts_rejects_mismatched_lengths()
    {
        string[] folders = { "a" };
        int[] counts = { 1, 2 };
        Action act = () => Utility.SortByCounts(folders, counts);

        act.Should().Throw<ArgumentException>();
    }
}
