using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of <see cref="OpfModel.MoveResourceList"/> — moving resources between
/// subfolders with a single reference update.
/// </summary>
public sealed class OpfModelMoveListTests
{
    private static Book Load(string corpusDir, TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(corpusDir, temp)).GetBook();

    [Fact]
    public void MoveResourceList_MovesResourceAndUpdatesCrossReferences()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        HtmlResource chapter = book.GetHtmlResources().Single(r => r.Filename == "chapter1.xhtml");
        string oldGroupFolder = Signet.Core.BookPath.StartingDir(image.BookPath);

        bool ok = model.MoveResourceList(new Resource[] { image }, oldGroupFolder + "/sub", out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        image.BookPath.Should().Be(oldGroupFolder + "/sub/figure.png");
        chapter.GetText().Should().Contain("figure.png");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void MoveResourceList_ToEpubRoot_UsesEmptyFolder()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");

        bool ok = model.MoveResourceList(new Resource[] { image }, string.Empty, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        image.BookPath.Should().Be("figure.png");
    }

    [Fact]
    public void MoveResourceList_DuplicateFilenameAtDestination_FailsWithoutMovingAnything()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource figure = book.GetAllResources().Single(r => r.Filename == "figure.png");
        Resource cover = book.GetAllResources().Single(r => r.Filename == "cover.png");
        string imagesFolder = Signet.Core.BookPath.StartingDir(figure.BookPath);

        // "figure.png" goes to the "sub" subfolder; "cover.png" is renamed to "figure.png"
        // in its current folder (no collision, because the original "figure.png" has already moved),
        // and then we try to move it to where "figure.png" already is — a real name collision.
        model.MoveResourceList(new Resource[] { figure }, imagesFolder + "/sub", out _).Should().BeTrue();
        model.RenameResource(cover, "figure.png", out _).Should().BeTrue();

        bool ok = model.MoveResourceList(new Resource[] { cover }, imagesFolder + "/sub", out string? error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        cover.BookPath.Should().Be(imagesFolder + "/figure.png");
    }

    [Fact]
    public void MoveResourceList_InvalidPathTraversal_Fails()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");

        bool ok = model.MoveResourceList(new Resource[] { image }, "../outside", out string? error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public void MoveResourceList_SameFolder_IsNoOpAndReportsSuccess()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        string folder = Signet.Core.BookPath.StartingDir(image.BookPath);

        bool ok = model.MoveResourceList(new Resource[] { image }, folder, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        book.Modified.Should().BeFalse();
    }

    [Fact]
    public void MoveResourceList_RegistersNewFolderForGroup()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        string oldGroupFolder = Signet.Core.BookPath.StartingDir(image.BookPath);
        string group = MediaTypes.GetGroupFromMediaType(image.MediaType, "other");

        bool ok = model.MoveResourceList(new Resource[] { image }, oldGroupFolder + "/sub", out _);

        ok.Should().BeTrue();
        IReadOnlyList<string> folders = book.GetFolderKeeper().GetFoldersForGroup(group);
        folders.Should().Contain(oldGroupFolder + "/sub");
    }
}
