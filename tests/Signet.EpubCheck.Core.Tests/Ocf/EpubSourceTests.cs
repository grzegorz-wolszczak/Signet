using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

public sealed class EpubSourceTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void ZipEpubSource_KeepsTheOrderOfTheCentralDirectoryAndTheContents()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("book.epub");
        string content = _fixture.Create<string>();
        TestZip.WriteEpub(path, TestZip.Entry.Of("z.xhtml", content), TestZip.Entry.Of("a/", string.Empty));

        // Act
        using ZipEpubSource source = new(path);
        using StreamReader reader = new(source.OpenRead(source.Entries[1]), Encoding.UTF8);

        // Assert
        source.Entries.Select(entry => (entry.Name, entry.Order, entry.IsDirectory))
            .Should().Equal(("mimetype", 0, false), ("z.xhtml", 1, false), ("a/", 2, true));
        source.Entries[1].CompressionMethod.Should().Be(CompressionMethod.Stored);
        source.Entries[1].Length.Should().Be(Encoding.UTF8.GetByteCount(content));
        reader.ReadToEnd().Should().Be(content);
    }

    [Fact]
    public void ZipEpubSource_NameThatIsNotUtf8_Throws()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("book.epub");
        TestZip.WriteEpub(path, new TestZip.Entry([0xFF, 0xFE], []));

        // Act & Assert
        FluentActions.Invoking(() => new ZipEpubSource(path)).Should().Throw<EpubZipEncodingException>();
    }

    [Fact]
    public void ZipEpubSource_FileWithoutEndOfCentralDirectory_ThrowsWithJavasText()
    {
        // Arrange
        using TempDir dir = new();
        string path = dir.Combine("book.epub");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("PK" + _fixture.Create<string>()));

        // Act & Assert
        FluentActions.Invoking(() => new ZipEpubSource(path)).Should().Throw<InvalidDataException>()
            .WithMessage(ZipEpubSource.NoEndHeaderMessage);
    }

    [Fact]
    public void DirectoryEpubSource_ListsFilesAndDirectoriesInOrdinalOrder()
    {
        // Arrange
        using TempDir dir = new();
        Directory.CreateDirectory(dir.Combine("b", "c"));
        File.WriteAllText(dir.Combine("b", "c", "d.xhtml"), _fixture.Create<string>());
        File.WriteAllText(dir.Combine("a.xhtml"), _fixture.Create<string>());

        // Act
        using DirectoryEpubSource source = new(dir.Path);

        // Assert
        source.Entries.Select(entry => entry.Name).Should().Equal("a.xhtml", "b/", "b/c/", "b/c/d.xhtml");
        source.Entries.Should().OnlyContain(entry => entry.Timestamp != null);
    }

    [Fact]
    public void DirectoryEpubSource_SymbolicLink_Throws()
    {
        // Arrange
        using TempDir dir = new();
        File.WriteAllText(dir.Combine("a.xhtml"), _fixture.Create<string>());
        File.CreateSymbolicLink(dir.Combine("link.xhtml"), dir.Combine("a.xhtml"));

        // Act & Assert
        FluentActions.Invoking(() => new DirectoryEpubSource(dir.Path)).Should().Throw<IOException>()
            .WithMessage(DirectoryEpubSource.SymbolicLinkMessage);
    }

    [Fact]
    public void MemoryEpubSource_OpensTheBytesOfAnEntry()
    {
        // Arrange
        byte[] content = _fixture.CreateMany<byte>(10).ToArray();
        using MemoryEpubSource source = new([new KeyValuePair<string, byte[]>("a.bin", content)]);

        // Act
        using MemoryStream copy = new();
        source.OpenRead(source.Entries[0]).CopyTo(copy);

        // Assert
        copy.ToArray().Should().Equal(content);
    }

    [Fact]
    public void MemoryEpubSource_EntryOfAnotherSource_Throws()
    {
        // Arrange
        using MemoryEpubSource source = new([new KeyValuePair<string, byte[]>("a.bin", [])]);
        EpubSourceEntry foreign = new("b.bin", 0, CompressionMethod.Stored, 0, 0, false);

        // Act & Assert
        FluentActions.Invoking(() => source.OpenRead(foreign)).Should().Throw<ArgumentException>();
    }
}
