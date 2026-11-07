using System;
using System.IO;
using System.Linq;
using System.Text;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="BookEpubSource"/>: the book as EPUBCheck reads it.</summary>
public sealed class BookEpubSourceTests
{
    private static Book Load(TempDir temp, string folder)
    {
        string epub = EpubBuilder.BuildInto(folder, temp);
        return new ImportEpub(epub).GetBook();
    }

    private static string ReadText(BookEpubSource source, string name)
    {
        EpubSourceEntry entry = source.Entries.Single(e => e.Name == name);
        using StreamReader reader = new(source.OpenRead(entry), new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(BookEpubSource source, string name)
    {
        using Stream stream = source.OpenRead(source.Entries.Single(e => e.Name == name));
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void Create_PutsTheMimetypeFirst_AndListsTheFilesOfTheBook()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);

        // Act
        using BookEpubSource sut = BookEpubSource.Create(book);

        // Assert
        sut.IsPackaged.Should().BeFalse();
        sut.Entries[0].Name.Should().Be("mimetype");
        sut.Entries[0].CompressionMethod.Should().Be(CompressionMethod.Stored);
        ReadText(sut, "mimetype").Should().Be("application/epub+zip");
        sut.Entries.Select(e => e.Name).Should().Contain("META-INF/container.xml");
        sut.Entries.Select(e => e.Name).Should().Contain(book.GetAllResources().Select(r => r.BookPath));
        sut.Entries.Select(e => e.Order).Should().Equal(Enumerable.Range(0, sut.Entries.Count));
    }

    [Fact]
    public void Create_DoesNotListTheLockFileOrTheStateFile()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        File.WriteAllText(Path.Combine(book.GetFolderKeeper().MainFolderPath, BookStateFile.FileName), "<signet_state />");

        // Act
        using BookEpubSource sut = BookEpubSource.Create(book);

        // Assert
        sut.Entries.Select(e => e.Name).Should().NotContain(n => n == TempFolder.LockFileName || BookStateFile.IsStateFile(n));
    }

    [Fact]
    public void Create_GivesTheTextThatIsOnlyInMemory_AndLeavesTheFileOnDiskAlone()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        string onDisk = File.ReadAllText(html.FullPath);
        string edited = new Fixture().Create<string>() + "\r\nsecond line";
        html.SetText(edited);

        // Act
        using BookEpubSource sut = BookEpubSource.Create(book);

        // Assert
        ReadText(sut, html.BookPath).Should().Be(Utility.ConvertLineEndingsAndNormalize(edited));
        sut.Entries.Single(e => e.Name == html.BookPath).Length.Should().Be(Encoding.UTF8.GetByteCount(Utility.ConvertLineEndingsAndNormalize(edited)));
        File.ReadAllText(html.FullPath).Should().Be(onDisk);
    }

    [Fact]
    public void Create_OfACleanBook_IsCheckedWithoutErrors()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        CollectingReport report = new();
        report.SetLocale(System.Globalization.CultureInfo.GetCultureInfo("en"));

        // Act
        using (BookEpubSource sut = BookEpubSource.Create(book))
        {
            OCFChecker.Check(sut, EpubVersion.Unknown, report, new EpubLocation("book.epub"));
        }

        // Assert
        report.FatalErrorCount.Should().Be(0);
        report.ErrorCount.Should().Be(0, string.Join("; ", report.Messages.Where(m => m.Message.Severity >= EpubCheck.Core.Messages.Severity.Error).Select(m => m.Text)));
    }

    [Fact]
    public void Create_CheckedWithTheUnsavedText_SeesTheProblemThatIsOnlyInMemory()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        HtmlResource html = book.GetHtmlResourcesExcludingNav().First();
        html.SetText("<html><body><p>not closed</body>");
        CollectingReport report = new();
        report.SetLocale(System.Globalization.CultureInfo.GetCultureInfo("en"));

        // Act
        using (BookEpubSource sut = BookEpubSource.Create(book))
        {
            OCFChecker.Check(sut, EpubVersion.Unknown, report, new EpubLocation("book.epub"));
        }

        // Assert
        report.Messages.Should().Contain(m => m.Location.Path == html.BookPath && m.Message.Severity >= EpubCheck.Core.Messages.Severity.Error);
    }

    [Fact]
    public void Create_WithObfuscatedFonts_AddsEncryptionXml_AndObfuscatesACopyOfTheFont()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Media);
        FontResource font = book.GetAllResources().OfType<FontResource>().First();
        book.SetFontObfuscation(font, OcfReader.IdpfFontAlgorithmId);
        byte[] onDisk = File.ReadAllBytes(font.FullPath);

        // Act
        using BookEpubSource sut = BookEpubSource.Create(book);

        // Assert
        ReadText(sut, "META-INF/encryption.xml").Should().Contain(font.BookPath);
        byte[] obfuscated = ReadBytes(sut, font.BookPath);
        obfuscated.Should().HaveCount(onDisk.Length);
        obfuscated.Should().NotEqual(onDisk);
        File.ReadAllBytes(font.FullPath).Should().Equal(onDisk);
    }

    [Fact]
    public void OpenRead_OfAnEntryOfAnotherSource_Throws()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        using BookEpubSource first = BookEpubSource.Create(book);
        using BookEpubSource second = BookEpubSource.Create(book);
        EpubSourceEntry foreign = first.Entries[0] with { Name = "other" };

        // Act & Assert
        Action act = () => second.OpenRead(foreign);
        act.Should().Throw<ArgumentException>();
    }
}
