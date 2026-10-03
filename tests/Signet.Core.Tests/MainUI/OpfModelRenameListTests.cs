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
/// Tests of <see cref="OpfModel.RenameResourceList"/> — bulk renaming with a single
/// reference update and a well-formedness safeguard.
/// </summary>
public sealed class OpfModelRenameListTests
{
    private static Book Load(string corpusDir, TempDir temp) =>
        new ImportEpub(EpubBuilder.BuildInto(corpusDir, temp)).GetBook();

    [Fact]
    public void RenameResourceList_RenamesEachResourceAndUpdatesCrossReferences()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource css = book.GetAllResources().Single(r => r.Filename == "style.css");
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");
        HtmlResource chapter = book.GetHtmlResources().Single(r => r.Filename == "chapter1.xhtml");

        Resource[] resources = { css, image };
        string[] newNames = { "style-new.css", "figure-new.png" };
        bool ok = model.RenameResourceList(resources, newNames, out IReadOnlyList<string> notRenamed, out IReadOnlyList<string> wellFormedErrors);

        ok.Should().BeTrue();
        notRenamed.Should().BeEmpty();
        wellFormedErrors.Should().BeEmpty();
        css.Filename.Should().Be("style-new.css");
        image.Filename.Should().Be("figure-new.png");
        chapter.GetText().Should().Contain("href=\"../styles/style-new.css\"");
        chapter.GetText().Should().Contain("src=\"../images/figure-new.png\"");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void RenameResourceList_DuplicateProposedName_SkipsThatEntryButRenamesOthers()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource css = book.GetAllResources().Single(r => r.Filename == "style.css");
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");

        Resource[] resources = { css, image };
        string[] newNames = { "style-new.css", "cover.png" }; // "cover.png" already used by another image in the same folder
        bool ok = model.RenameResourceList(resources, newNames, out IReadOnlyList<string> notRenamed, out _);

        ok.Should().BeFalse();
        notRenamed.Should().ContainSingle(p => p.EndsWith("figure.png"));
        css.Filename.Should().Be("style-new.css");
        image.Filename.Should().Be("figure.png");
    }

    [Fact]
    public void RenameResourceList_ForbiddenCharacterInOneEntry_SkipsOnlyThatEntry()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource css = book.GetAllResources().Single(r => r.Filename == "style.css");
        Resource image = book.GetAllResources().Single(r => r.Filename == "figure.png");

        Resource[] resources = { css, image };
        string[] newNames = { "bad:name.css", "figure-new.png" };
        bool ok = model.RenameResourceList(resources, newNames, out IReadOnlyList<string> notRenamed, out _);

        ok.Should().BeFalse();
        notRenamed.Should().ContainSingle(p => p.EndsWith("style.css"));
        css.Filename.Should().Be("style.css");
        image.Filename.Should().Be("figure-new.png");
    }

    [Fact]
    public void RenameResourceList_SameNameForAll_IsNoOpAndReportsSuccess()
    {
        using TempDir temp = new();
        using Book book = Load(CorpusPaths.Epub3Media, temp);
        using OpfModel model = new(book);
        Resource css = book.GetAllResources().Single(r => r.Filename == "style.css");
        Resource[] resources = { css };
        string[] newNames = { "style.css" };

        bool ok = model.RenameResourceList(resources, newNames, out IReadOnlyList<string> notRenamed, out IReadOnlyList<string> wellFormedErrors);

        ok.Should().BeTrue();
        notRenamed.Should().BeEmpty();
        wellFormedErrors.Should().BeEmpty();
        book.Modified.Should().BeFalse();
    }
}
