using System.Collections.Generic;
using System.IO;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

/// <summary>The container of an OCF publication: its files by URL, encryption, what is remote, and the context built on it.</summary>
public sealed class OCFContainerTests
{
    [Fact]
    public void Contains_FileWithAnAccentedName_IsFoundByItsEncodedUrl()
    {
        // Arrange
        using MemoryEpubSource source = Source("EPUB/café 1.xhtml");
        OCFContainer container = Build(source);

        // Act & Assert
        container.Contains(container.RootUrl.Resolve("EPUB/caf%C3%A9%201.xhtml")).Should().BeTrue();
        container.Contains(container.RootUrl.Resolve("EPUB/other.xhtml")).Should().BeFalse();
    }

    [Fact]
    public void OpenStream_File_GivesItsContent()
    {
        // Arrange
        using MemoryEpubSource source = Source("a.txt");
        OCFContainer container = Build(source);

        // Act
        using Stream stream = container.OpenStream(container.RootUrl.Resolve("a.txt"));

        // Assert
        new StreamReader(stream).ReadToEnd().Should().Be("content of a.txt");
    }

    [Fact]
    public void OpenStream_FileThatIsNotThere_Throws()
    {
        // Arrange
        using MemoryEpubSource source = Source("a.txt");
        OCFContainer container = Build(source);

        // Act & Assert
        container.Invoking(c => c.OpenStream(c.RootUrl.Resolve("b.txt"))).Should().Throw<System.ArgumentException>();
    }

    [Theory]
    [InlineData(EncryptionKind.IdpfFontObfuscation, false)]
    [InlineData(EncryptionKind.AdobeFontObfuscation, false)]
    [InlineData(EncryptionKind.Unsupported, false)]
    public void CanDecrypt_IsFalseForEveryEncryption_AsUpstreamIs(EncryptionKind kind, bool expected)
    {
        // Arrange
        using MemoryEpubSource source = Source("font.otf");
        OCFContainer.Builder builder = new(source);
        builder.AddResource(source.Entries[0]);
        Url font = builder.RootUrl.Resolve("font.otf");
        builder.AddEncryption(font, kind);
        OCFContainer container = builder.Build();

        // Act & Assert
        container.CanDecrypt(font).Should().Be(expected);
    }

    [Fact]
    public void IsRemote_UrlOutsideTheContainer_IsRemoteButDataAndContainedAreNot()
    {
        // Arrange
        using MemoryEpubSource source = Source("a.txt");
        OCFContainer container = Build(source);

        // Act & Assert
        container.IsRemote(Url.Parse("https://example.org/x")).Should().BeTrue();
        container.IsRemote(Url.Parse("data:text/plain,x")).Should().BeFalse();
        container.IsRemote(container.RootUrl.Resolve("a.txt")).Should().BeFalse();
    }

    [Fact]
    public void Path_ResourceOfTheContainer_IsRelativeToItsRoot()
    {
        // Arrange
        using MemoryEpubSource source = Source("EPUB/a.xhtml");
        OCFContainer container = Build(source);
        ValidationContext context = new() { Url = container.RootUrl.Resolve("EPUB/a.xhtml"), Report = new CollectingReport(), Container = container };

        // Act & Assert
        context.Path.Should().Be("EPUB/a.xhtml");
        context.Location().Path.Should().Be("EPUB/a.xhtml");
    }

    [Fact]
    public void Path_LocalFileWithoutAContainer_IsTheFilePath()
    {
        // Arrange
        ValidationContext context = new() { Url = Url.Parse("file:///tmp/books/a.xhtml"), Report = new CollectingReport() };

        // Act & Assert
        context.Path.Replace('\\', '/').Should().EndWith("/tmp/books/a.xhtml");
    }

    private static OCFContainer Build(MemoryEpubSource source)
    {
        OCFContainer.Builder builder = new(source);
        foreach (EpubSourceEntry entry in source.Entries)
        {
            builder.AddResource(entry);
        }

        return builder.Build();
    }

    private static MemoryEpubSource Source(params string[] names)
    {
        List<KeyValuePair<string, byte[]>> entries = [];
        foreach (string name in names)
        {
            entries.Add(new(name, Encoding.UTF8.GetBytes("content of " + name)));
        }

        return new MemoryEpubSource(entries);
    }
}
