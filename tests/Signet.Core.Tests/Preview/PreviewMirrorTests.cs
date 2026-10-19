using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Preview;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Preview;

/// <summary>Tests for <see cref="PreviewMirror"/> — the layer that serves book content to the preview over <c>file://</c>.</summary>
public sealed class PreviewMirrorTests
{
    public static TheoryData<string> ValidCorpus()
    {
        TheoryData<string> data = new();
        foreach (string dir in CorpusPaths.ValidEpubDirs)
        {
            data.Add(dir);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ValidCorpus))]
    public void Sync_ValidCorpus_WritesEveryResourceUnderRoot(string corpusDir)
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(corpusDir, temp);
        using Book book = new ImportEpub(epub).GetBook();

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book);

        // Assert
        foreach (Resource resource in book.GetAllResources())
        {
            string expected = Path.Combine(
                mirror.RootPath,
                resource.BookPath.Replace('/', Path.DirectorySeparatorChar));

            File.Exists(expected).Should().BeTrue($"resource {resource.BookPath} should be written to the mirror");
        }
    }

    [Fact]
    public void Sync_CopiesBinaryResourcesByteForByte()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book);

        // Assert
        Resource[] binary = book.GetAllResources().Where(r => r is not TextResource).ToArray();
        binary.Should().NotBeEmpty("the 'media' fixture has images, a font and audio");

        foreach (Resource resource in binary)
        {
            string mirrored = Path.Combine(
                mirror.RootPath,
                resource.BookPath.Replace('/', Path.DirectorySeparatorChar));

            File.ReadAllBytes(mirrored).Should().Equal(File.ReadAllBytes(resource.FullPath));
        }
    }

    [Fact]
    public void Sync_WritesCurrentInMemoryText_IncludingUnsavedEdits()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        using Book book = new ImportEpub(epub).GetBook();

        HtmlResource html = book.GetHtmlResources().First();
        const string marker = "<!-- signet-preview-marker -->";
        html.SetText(html.GetText() + marker);

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book);

        string mirrored = Path.Combine(
            mirror.RootPath,
            html.BookPath.Replace('/', Path.DirectorySeparatorChar));

        // Assert
        File.ReadAllText(mirrored).Should().Contain(marker);
        File.ReadAllText(html.FullPath).Should().NotContain(marker, "the edit has not been saved to the book on disk");
    }

    [Fact]
    public void Sync_AfterResourceRemoved_PrunesStaleFile()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        Resource victim = book.GetAllResources().First(r => r is ImageResource);
        string relative = victim.BookPath.Replace('/', Path.DirectorySeparatorChar);

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book);

        // Assert
        File.Exists(Path.Combine(mirror.RootPath, relative)).Should().BeTrue();

        // Act
        folderKeeper.RemoveResource(victim);
        mirror.Sync(book);

        // Assert
        File.Exists(Path.Combine(mirror.RootPath, relative)).Should().BeFalse("the file of a removed resource should disappear from the mirror");
    }

    [Fact]
    public void UrlForBookPath_IsFileUriUnderRoot()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        using Book book = new ImportEpub(epub).GetBook();

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book);

        // Arrange
        HtmlResource html = book.GetHtmlResources().First();

        // Act
        Uri url = mirror.UrlFor(html);

        // Assert
        url.IsFile.Should().BeTrue();
        url.Scheme.Should().Be("file");
        mirror.IsInsideMirror(url).Should().BeTrue();
    }

    [Fact]
    public void IsInsideMirror_DistinguishesInternalFromExternal()
    {
        // Arrange
        using PreviewMirror mirror = new();

        // Act & Assert
        mirror.IsInsideMirror(mirror.UrlForBookPath("Text/chapter.xhtml")).Should().BeTrue();

        // Act & Assert
        mirror.IsInsideMirror(new Uri("https://example.com/page.html")).Should().BeFalse();

        // Act & Assert
        mirror.IsInsideMirror(new Uri(Path.Combine(Path.GetTempPath(), "other", "file.xhtml"))).Should().BeFalse();

        // Act & Assert
        mirror.IsInsideMirror(null).Should().BeFalse();
    }

    [Fact]
    public void BookPathForUri_RoundTripsWithUrlForBookPath()
    {
        // Arrange
        using PreviewMirror mirror = new();

        const string bookPath = "OEBPS/Text/rozdział 1.xhtml";

        // Act
        Uri url = mirror.UrlForBookPath(bookPath);

        // Assert
        mirror.BookPathForUri(url).Should().Be(bookPath);
        mirror.BookPathForUri(new Uri("https://example.com/")).Should().BeNull();
    }

    [Fact]
    public void Sync_WithInstrumentBookPath_InstrumentsOnlyThatResource()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();

        HtmlResource[] htmls = book.GetHtmlResources().ToArray();

        htmls.Length.Should().BeGreaterThan(1, "the 'media' fixture has several chapters");

        HtmlResource active = htmls[0];
        HtmlResource other = htmls[1];

        using PreviewMirror mirror = new();

        // Act
        PreviewInstrumentation? instrumentation = mirror.Sync(book, active.BookPath);

        // Assert
        instrumentation.Should().NotBeNull();
        instrumentation!.Locs.Should().NotBeEmpty();

        // Arrange
        string activeFile = Path.Combine(mirror.RootPath, active.BookPath.Replace('/', Path.DirectorySeparatorChar));
        string otherFile = Path.Combine(mirror.RootPath, other.BookPath.Replace('/', Path.DirectorySeparatorChar));

        // Assert
        File.ReadAllText(activeFile).Should().Contain(PreviewInstrumentation.LocAttribute);
        File.ReadAllText(otherFile).Should().NotContain(PreviewInstrumentation.LocAttribute);
    }

    [Fact]
    public void Sync_WithNullInstrumentBookPath_ReturnsNull()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        using Book book = new ImportEpub(epub).GetBook();

        using PreviewMirror mirror = new();

        // Act & Assert
        mirror.Sync(book, instrumentBookPath: null).Should().BeNull();
    }

    [Theory]
    [AutoData]
    public void Sync_WithTextOverride_WritesOverrideAndLeavesResourceUntouched(string marker)
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();

        HtmlResource html = book.GetHtmlResources().First();
        CssResource css = book.GetAllResources().OfType<CssResource>().First();
        string htmlBefore = html.GetText();
        string cssBefore = css.GetText();
        string workingHtml = htmlBefore.Replace("</body>", $"<p>{marker}</p></body>", StringComparison.Ordinal);
        string workingCss = cssBefore + $"\n/* {marker} */\n";
        Dictionary<string, string> overrides = new(StringComparer.Ordinal)
        {
            [html.BookPath] = workingHtml,
            [css.BookPath] = workingCss,
        };

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book, instrumentBookPath: null, overrides);

        // Assert
        File.ReadAllText(MirroredPath(mirror, html)).Should().Be(workingHtml);
        File.ReadAllText(MirroredPath(mirror, css)).Should().Be(workingCss);
        html.GetText().Should().Be(htmlBefore, "the preview must not write the working text into the resource");
        css.GetText().Should().Be(cssBefore);
    }

    [Theory]
    [AutoData]
    public void Sync_WithTextOverrideForInstrumentedResource_InstrumentsOverride(string marker)
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        using Book book = new ImportEpub(epub).GetBook();

        HtmlResource html = book.GetHtmlResources().First();
        string working = html.GetText().Replace("</body>", $"<p>{marker}</p></body>", StringComparison.Ordinal);
        Dictionary<string, string> overrides = new(StringComparer.Ordinal) { [html.BookPath] = working };

        using PreviewMirror mirror = new();

        // Act
        PreviewInstrumentation? instrumentation = mirror.Sync(book, html.BookPath, overrides);

        // Arrange
        PreviewInstrumentation expected = PreviewInstrumentation.Create(working);

        // Assert
        instrumentation.Should().NotBeNull();
        instrumentation!.Html.Should().Be(expected.Html);
        instrumentation.Locs.Should().Equal(expected.Locs);
        File.ReadAllText(MirroredPath(mirror, html)).Should().Be(expected.Html).And.Contain(marker);
    }

    [Theory]
    [AutoData]
    public void Sync_WithOverrideForUnknownBookPath_IgnoresIt(string unknownBookPath, string text)
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp);
        using Book book = new ImportEpub(epub).GetBook();
        Dictionary<string, string> overrides = new(StringComparer.Ordinal) { [unknownBookPath] = text };

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book, instrumentBookPath: null, overrides);

        // Assert
        File.Exists(Path.Combine(mirror.RootPath, unknownBookPath)).Should().BeFalse();
        foreach (TextResource resource in book.GetAllResources().OfType<TextResource>())
        {
            File.ReadAllText(MirroredPath(mirror, resource)).Should().Be(resource.GetText());
        }
    }

    [Fact]
    public void Sync_Again_WithoutChanges_DoesNotRewriteAnyFile()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book, book.GetHtmlResources().First().BookPath);

        // Arrange
        StampAllMirrorFiles(mirror);

        // Act
        mirror.Sync(book, book.GetHtmlResources().First().BookPath);

        // Assert
        RewrittenFiles(mirror).Should().BeEmpty("unchanged resources are not rewritten");
    }

    [Theory]
    [AutoData]
    public void Sync_AfterOneResourceChanged_RewritesOnlyThatFile(string marker)
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();
        HtmlResource html = book.GetHtmlResources().First();

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book);

        // Arrange
        StampAllMirrorFiles(mirror);

        Dictionary<string, string> overrides = new(StringComparer.Ordinal)
        {
            [html.BookPath] = html.GetText().Replace("</body>", $"<p>{marker}</p></body>", StringComparison.Ordinal),
        };

        // Act
        mirror.Sync(book, instrumentBookPath: null, overrides);

        // Assert
        RewrittenFiles(mirror).Should().Equal(MirroredPath(mirror, html));
        File.ReadAllText(MirroredPath(mirror, html)).Should().Contain(marker);
    }

    [Fact]
    public void Sync_WhenInstrumentedResourceChanges_RewritesBothOldAndNewOne()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();
        HtmlResource first = book.GetHtmlResources().First();
        HtmlResource second = book.GetHtmlResources().Skip(1).First();

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book, first.BookPath);

        // Arrange
        StampAllMirrorFiles(mirror);

        // Act
        mirror.Sync(book, second.BookPath);

        // Assert
        RewrittenFiles(mirror).Should().BeEquivalentTo(new[] { MirroredPath(mirror, first), MirroredPath(mirror, second) });
        File.ReadAllText(MirroredPath(mirror, first)).Should().NotContain(PreviewInstrumentation.LocAttribute);
        File.ReadAllText(MirroredPath(mirror, second)).Should().Contain(PreviewInstrumentation.LocAttribute);
    }

    [Fact]
    public void Sync_RestoresMirrorFileDeletedFromDisk()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();
        Resource text = book.GetHtmlResources().First();
        Resource binary = book.GetAllResources().First(r => r is ImageResource);

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book);
        File.Delete(MirroredPath(mirror, text));
        File.Delete(MirroredPath(mirror, binary));

        mirror.Sync(book);

        // Assert
        File.Exists(MirroredPath(mirror, text)).Should().BeTrue();
        File.ReadAllBytes(MirroredPath(mirror, binary)).Should().Equal(File.ReadAllBytes(binary.FullPath));
    }

    [Fact]
    public void Sync_AfterRemovedResourceIsReAdded_WritesItAgain()
    {
        // Arrange
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();
        FolderKeeper folderKeeper = book.GetFolderKeeper();
        HtmlResource victim = book.GetHtmlResources().Last();
        string bookPath = victim.BookPath;
        string content = victim.GetText();

        using PreviewMirror mirror = new();

        // Act
        mirror.Sync(book);
        folderKeeper.RemoveResource(victim);
        mirror.Sync(book);

        // Assert
        File.Exists(Path.Combine(mirror.RootPath, bookPath.Replace('/', Path.DirectorySeparatorChar))).Should().BeFalse();

        // Arrange
        string restoredFile = Path.Combine(temp.Path, "restored.xhtml");
        File.WriteAllText(restoredFile, content);

        // Act
        Resource restored = folderKeeper.AddContentFileToFolder(restoredFile, bookPath: bookPath);
        mirror.Sync(book);

        // Assert
        File.Exists(MirroredPath(mirror, restored)).Should().BeTrue("a re-added resource comes back to the mirror");
    }

    [Fact]
    public void Dispose_OwnedFolder_RemovesDirectory()
    {
        // Arrange
        PreviewMirror mirror = new();
        string root = mirror.RootPath;

        Directory.Exists(root).Should().BeTrue();

        // Act
        mirror.Dispose();

        // Assert
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void Dispose_BorrowedFolder_LeavesDirectory()
    {
        // Arrange
        using TempFolder folder = new();
        PreviewMirror mirror = new(folder);

        // Act
        mirror.Dispose();

        // Assert
        Directory.Exists(folder.Path).Should().BeTrue();
    }

    private static readonly DateTime Stamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Resets the modification time of every mirror file to a fixed date so the next write is visible
    // regardless of the file system's clock resolution.
    private static void StampAllMirrorFiles(PreviewMirror mirror)
    {
        foreach (string file in Directory.EnumerateFiles(mirror.RootPath, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, Stamp);
        }
    }

    private static string[] RewrittenFiles(PreviewMirror mirror) =>
        Directory.EnumerateFiles(mirror.RootPath, "*", SearchOption.AllDirectories)
            .Where(file => File.GetLastWriteTimeUtc(file) != Stamp)
            .ToArray();

    private static string MirroredPath(PreviewMirror mirror, Resource resource) =>
        Path.Combine(mirror.RootPath, resource.BookPath.Replace('/', Path.DirectorySeparatorChar));
}
