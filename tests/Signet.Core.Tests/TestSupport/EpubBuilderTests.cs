using System.IO;
using System.IO.Compression;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests.TestSupport;

/// <summary>Tests of packing the corpus into an <c>.epub</c> archive.</summary>
public sealed class EpubBuilderTests
{
    [Theory]
    [MemberData(nameof(ValidEpubDirs))]
    public void Builds_every_valid_fixture_into_a_readable_archive(string dir)
    {
        byte[] bytes = EpubBuilder.BuildBytes(dir);

        using ZipArchive zip = new(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.Entries.Should().NotBeEmpty();

        string[] onDisk = Directory
            .EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(x => x)
            .ToArray();

        zip.Entries.Select(e => e.FullName).OrderBy(x => x).Should().Equal(onDisk);
    }

    [Fact]
    public void Mimetype_is_first_and_stored_uncompressed()
    {
        byte[] bytes = EpubBuilder.BuildBytes(CorpusPaths.Epub3Minimal);

        using ZipArchive zip = new(new MemoryStream(bytes), ZipArchiveMode.Read);

        zip.Entries[0].FullName.Should().Be("mimetype");

        ZipArchiveEntry mimetype = zip.Entries[0];
        mimetype.CompressedLength.Should().Be(mimetype.Length, "the mimetype entry must be uncompressed (STORED)");

        using StreamReader reader = new(mimetype.Open());
        reader.ReadToEnd().Should().Be("application/epub+zip");
    }

    [Fact]
    public void Build_is_deterministic_in_entry_order()
    {
        byte[] first = EpubBuilder.BuildBytes(CorpusPaths.Epub3WithNcx);
        byte[] second = EpubBuilder.BuildBytes(CorpusPaths.Epub3WithNcx);

        static string[] Order(byte[] b)
        {
            using ZipArchive zip = new(new MemoryStream(b), ZipArchiveMode.Read);
            return zip.Entries.Select(e => e.FullName).ToArray();
        }

        Order(first).Should().Equal(Order(second));
    }

    [Fact]
    public void BuildInto_writes_a_file_named_after_the_source_directory()
    {
        using TempDir temp = new();

        string path = EpubBuilder.BuildInto(CorpusPaths.Epub2Minimal, temp);

        Path.GetFileName(path).Should().Be("minimal.epub");
        File.Exists(path).Should().BeTrue();
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
