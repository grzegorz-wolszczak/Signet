using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Localization;
using Signet.Core.Tests.TestSupport;
using Xunit;
using SysPath = System.IO.Path;

namespace Signet.Core.Tests;

/// <summary>
/// An EPUB that is damaged still loads what can be loaded (Calibre 2470482fa3, 88391ed54d): a ZIP without its central directory is read from
/// the headers of its entries, a rootfile that is not in the book is skipped for one that is; where nothing can be loaded the message names
/// what is missing.
/// </summary>
public sealed class OcfRecoveryTests
{
    // The EPUB tree as a ZIP in memory. A seekable output gets the sizes in the local headers, a non-seekable one (a download, a pipe) data
    // descriptors after the data.
    private static byte[] Zip(string tree, bool seekable, CompressionLevel level)
    {
        using MemoryStream output = new();
        using Stream target = seekable ? output : new WriteOnlyStream(output);
        using (ZipArchive archive = new(target, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "mimetype", File.ReadAllBytes(SysPath.Combine(tree, "mimetype")), CompressionLevel.NoCompression);
            foreach (string file in Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string name = SysPath.GetRelativePath(tree, file).Replace('\\', '/');
                if (name != "mimetype")
                {
                    Add(archive, name, File.ReadAllBytes(file), level);
                }
            }
        }

        return output.ToArray();
    }

    private static void Add(ZipArchive archive, string name, byte[] data, CompressionLevel level)
    {
        using Stream entry = archive.CreateEntry(name, level).Open();
        entry.Write(data);
    }

    // The ZIP as a download that stopped before the central directory: everything before it is intact.
    private static byte[] WithoutCentralDirectory(byte[] zip)
    {
        int endOfCentralDirectory = zip.Length - 22;
        BitConverter.ToUInt32(zip, endOfCentralDirectory).Should().Be(0x06054b50u, "the archive has no comment");
        return zip[..(int)BitConverter.ToUInt32(zip, endOfCentralDirectory + 16)];
    }

    private static string Epub(TempDir temp, byte[] bytes)
    {
        string path = temp.Combine("book.epub");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // A stream that cannot seek, so that ZipArchive writes data descriptors.
    private sealed class WriteOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    // --- a ZIP without its central directory ---

    [Theory]
    [InlineData(true, CompressionLevel.NoCompression)]
    [InlineData(true, CompressionLevel.Optimal)]
    [InlineData(false, CompressionLevel.NoCompression)]
    [InlineData(false, CompressionLevel.Optimal)]
    public void A_zip_without_its_central_directory_is_read_from_its_local_headers(bool seekable, CompressionLevel level)
    {
        // Arrange
        using TempDir temp = new();
        using TempDir work = new();
        string epub = Epub(temp, WithoutCentralDirectory(Zip(CorpusPaths.Epub3Minimal, seekable, level)));
        string pristine = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp, "pristine.epub");
        new Action(() => ZipFile.OpenRead(epub).Dispose()).Should().Throw<InvalidDataException>("the archive really is damaged");

        // Act
        OcfContainer container = new OcfReader(epub).Extract(work.Path);

        // Assert
        container.OpfBookPath.Should().Be("EPUB/package.opf");
        container.Warnings.Should().ContainSingle().Which.Should().Be(CoreStrings.Format("LoadWarning_DamagedZipRecovered", 6));
        using ZipArchive original = ZipFile.OpenRead(pristine);
        foreach (ZipArchiveEntry entry in original.Entries.Where(e => e.Length > 0))
        {
            File.ReadAllBytes(SysPath.Combine(work.Path, entry.FullName.Replace('/', SysPath.DirectorySeparatorChar)))
                .Should().Equal(ReadAll(entry), entry.FullName);
        }
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void A_book_whose_zip_is_damaged_loads_as_a_book()
    {
        // Arrange
        using TempDir temp = new();
        string epub = Epub(temp, WithoutCentralDirectory(Zip(CorpusPaths.Epub3Minimal, seekable: true, CompressionLevel.Optimal)));

        // Act
        using Book book = new ImportEpub(epub).GetBook();

        // Assert
        book.GetHtmlResources().Select(h => h.Filename).Should().Contain("chapter1.xhtml");
        book.LoadWarnings.Should().Contain(CoreStrings.Format("LoadWarning_DamagedZipRecovered", 6));
    }

    [Fact]
    public void A_zip_with_nothing_to_recover_is_still_a_corrupt_zip()
    {
        // Arrange
        using TempDir temp = new();
        using TempDir work = new();
        string epub = Epub(temp, [.. "this is not a zip file at all, only text"u8]);

        // Act
        Action act = () => new OcfReader(epub).Extract(work.Path);

        // Assert
        act.Should().Throw<EpubLoadException>().WithMessage(CoreStrings.Format("LoadError_CorruptZip", epub));
    }

    [Fact]
    public void A_damaged_zip_without_container_or_opf_fails_and_the_message_names_the_container()
    {
        // Arrange
        using TempDir temp = new();
        using TempDir work = new();
        string tree = temp.Combine("tree");
        Directory.CreateDirectory(tree);
        File.WriteAllText(SysPath.Combine(tree, "mimetype"), "application/epub+zip");
        File.WriteAllText(SysPath.Combine(tree, "readme.txt"), "x");
        string epub = Epub(temp, WithoutCentralDirectory(Zip(tree, seekable: true, CompressionLevel.Optimal)));

        // Act
        Action act = () => new OcfReader(epub).Extract(work.Path);

        // Assert
        act.Should().Throw<EpubLoadException>().WithMessage("*container.xml*");
    }

    // --- rootfiles that are not in the book ---

    [Fact]
    public void A_first_rootfile_that_is_missing_is_skipped_for_the_one_that_is_there()
    {
        // Arrange
        using TempDir temp = new();
        using TempDir work = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Malformed("first-rootfile-missing"), temp);

        // Act
        OcfContainer container = new OcfReader(epub).Extract(work.Path);

        // Assert
        container.OpfBookPath.Should().Be("EPUB/package.opf");
        container.Rootfiles.Should().Equal("EPUB/package.opf");
        container.Warnings.Should().ContainSingle().Which.Should().Be(CoreStrings.Format("LoadWarning_RootfileMissing", "EPUB/missing.opf"));
    }

    [Fact]
    public void A_second_rootfile_that_is_missing_is_ignored_with_a_warning()
    {
        // Arrange
        using TempDir temp = new();
        string tree = TestFs.CopyDirectory(CorpusPaths.Malformed("first-rootfile-missing"), temp.Combine("tree"));
        string container = SysPath.Combine(tree, "META-INF", "container.xml");
        File.WriteAllText(container, File.ReadAllText(container)
            .Replace("EPUB/missing.opf", "EPUB/TMP", StringComparison.Ordinal)
            .Replace("EPUB/package.opf", "EPUB/missing.opf", StringComparison.Ordinal)
            .Replace("EPUB/TMP", "EPUB/package.opf", StringComparison.Ordinal));
        using TempDir work = new();

        // Act
        OcfContainer read = OcfReader.ReadExtracted(tree);

        // Assert
        read.OpfBookPath.Should().Be("EPUB/package.opf");
        read.Rootfiles.Should().Equal("EPUB/package.opf");
        read.Warnings.Should().ContainSingle().Which.Should().Be(CoreStrings.Format("LoadWarning_RootfileMissing", "EPUB/missing.opf"));
    }

    [Fact]
    public void When_no_rootfile_is_in_the_book_the_message_names_them_all()
    {
        // Arrange
        using TempDir temp = new();
        string tree = TestFs.CopyDirectory(CorpusPaths.Malformed("first-rootfile-missing"), temp.Combine("tree"));
        File.Delete(SysPath.Combine(tree, "EPUB", "package.opf"));

        // Act
        Action act = () => OcfReader.ReadExtracted(tree);

        // Assert
        act.Should().Throw<EpubLoadException>().WithMessage("*EPUB/missing.opf*").WithMessage("*EPUB/package.opf*");
    }
}
