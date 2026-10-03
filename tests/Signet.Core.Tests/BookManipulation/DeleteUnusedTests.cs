using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of "Delete Unused Media Files" / "Delete Unused Stylesheet Selectors":
/// <see cref="Book.FindUnusedMediaResources"/>, <see cref="Book.DeleteMediaResources"/>,
/// <see cref="Book.FindUnusedStyleSelectors"/>, <see cref="Book.DeleteCssSelectors"/>.
/// </summary>
public sealed class DeleteUnusedTests
{
    private static Book LoadMedia(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        return new ImportEpub(epub).GetBook();
    }

    private static Book LoadModifiedMedia(TempDir temp, Action<string> mutate)
    {
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Media, tree);
        mutate(tree);
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
        return new ImportEpub(epub).GetBook();
    }

    private static Book LoadModifiedMinimal(TempDir temp, Action<string> mutate)
    {
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        mutate(tree);
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
        return new ImportEpub(epub).GetBook();
    }

    private static void AddOrphanImage(string tree, string filename, string id)
    {
        string opfPath = Path.Combine(tree, "EPUB", "package.opf");
        string opf = File.ReadAllText(opfPath);
        opf = opf.Replace(
            "  </manifest>",
            $"    <item id=\"{id}\" href=\"images/{filename}\" media-type=\"image/png\"/>\n  </manifest>",
            StringComparison.Ordinal);
        File.WriteAllText(opfPath, opf);

        File.Copy(
            Path.Combine(tree, "EPUB", "images", "cover.png"),
            Path.Combine(tree, "EPUB", "images", filename));
    }

    private static void AppendToChapter1Body(string tree, string html)
    {
        string path = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("</body>", html + "\n</body>", StringComparison.Ordinal));
    }

    private static void AppendCssToStyle(string tree, string css)
    {
        string path = Path.Combine(tree, "EPUB", "styles", "style.css");
        File.AppendAllText(path, "\n" + css + "\n");
    }

    private static void AddInlineStyleBlockToChapter1(string tree, string styleBlock)
    {
        string path = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("</head>", styleBlock + "\n</head>", StringComparison.Ordinal));
    }

    private static void AddClassAttributeToChapter1(string tree, string className)
    {
        string path = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(
            path,
            File.ReadAllText(path).Replace(
                "<p>Hello, world.</p>", $"<p class=\"{className}\">Hello, world.</p>", StringComparison.Ordinal));
    }

    // -----------------------------------------------------------------
    //  Delete Unused Media Files
    // -----------------------------------------------------------------

    [Fact]
    public void FindUnusedMediaResources_DoesNotFlagCoverImageOrUsedMedia()
    {
        using TempDir temp = new();
        using Book book = LoadMedia(temp);

        UnusedMediaResult result = book.FindUnusedMediaResources();

        result.Applied.Should().BeTrue();
        result.UnusedResources.Should().BeEmpty();
    }

    [Fact]
    public void FindUnusedMediaResources_FlagsResourceNotReferencedAnywhere()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMedia(temp, tree => AddOrphanImage(tree, "orphan.png", "orphan"));

        UnusedMediaResult result = book.FindUnusedMediaResources();

        result.Applied.Should().BeTrue();
        result.UnusedResources.Should().ContainSingle(r => r.BookPath.EndsWith("orphan.png", StringComparison.Ordinal));
    }

    [Fact]
    public void FindUnusedMediaResources_DoesNotFlagImageUsedOnlyViaInlineStyleAttribute()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMedia(temp, tree =>
        {
            AddOrphanImage(tree, "orphan2.png", "orphan2");
            AppendToChapter1Body(tree, "<div style=\"background-image:url(../images/orphan2.png)\"></div>");
        });

        UnusedMediaResult result = book.FindUnusedMediaResources();

        result.Applied.Should().BeTrue();
        result.UnusedResources.Should().NotContain(r => r.BookPath.EndsWith("orphan2.png", StringComparison.Ordinal));
    }

    [Fact]
    public void FindUnusedMediaResources_DoesNotFlagImageUsedOnlyViaCssBackgroundImage()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMedia(temp, tree =>
        {
            AddOrphanImage(tree, "orphan3.png", "orphan3");
            AppendCssToStyle(tree, ".bg { background-image: url(../images/orphan3.png); }");
        });

        UnusedMediaResult result = book.FindUnusedMediaResources();

        result.Applied.Should().BeTrue();
        result.UnusedResources.Should().NotContain(r => r.BookPath.EndsWith("orphan3.png", StringComparison.Ordinal));
    }

    [Fact]
    public void FindUnusedMediaResources_AbortsWhenAnyHtmlIsNotWellFormed()
    {
        using TempDir temp = new();
        using Book book = LoadMedia(temp);
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText("<html><head><body><p>broken");

        UnusedMediaResult result = book.FindUnusedMediaResources();

        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(chapter);
        result.UnusedResources.Should().BeEmpty();
    }

    [Fact]
    public void DeleteMediaResources_RemovesFromManifestAndDisk()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMedia(temp, tree => AddOrphanImage(tree, "orphan4.png", "orphan4"));
        Resource orphan = book.FindUnusedMediaResources().UnusedResources.Single();
        string fullPath = orphan.FullPath;

        book.DeleteMediaResources(new[] { orphan });

        book.GetMediaResources().Should().NotContain(orphan);
        File.Exists(fullPath).Should().BeFalse();
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void DeleteMediaResources_WithEmptyList_DoesNothing()
    {
        using TempDir temp = new();
        using Book book = LoadMedia(temp);

        book.DeleteMediaResources(Array.Empty<Resource>());

        book.Modified.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    //  Delete Unused Stylesheet Selectors
    // -----------------------------------------------------------------

    [Fact]
    public void FindUnusedStyleSelectors_AbortsWhenAnyHtmlIsNotWellFormed()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText("<html><head><body><p>broken");

        UnusedStyleSelectorsResult result = book.FindUnusedStyleSelectors();

        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(chapter);
        result.UnusedSelectors.Should().BeEmpty();
    }

    [Fact]
    public void FindUnusedStyleSelectors_FlagsSelectorNotUsedAnywhere()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree => AppendCssToStyle(tree, ".ghost { color: red }"));

        UnusedStyleSelectorsResult result = book.FindUnusedStyleSelectors();

        result.Applied.Should().BeTrue();
        result.UnusedSelectors.Select(s => s.SelectorText).Should().Equal(".ghost");
    }

    [Fact]
    public void FindUnusedStyleSelectors_DoesNotFlagSelectorMatchingClassAttribute()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            AppendCssToStyle(tree, ".tag { color: green }");
            AddClassAttributeToChapter1(tree, "tag");
        });

        UnusedStyleSelectorsResult result = book.FindUnusedStyleSelectors();

        result.UnusedSelectors.Should().NotContain(s => s.SelectorText == ".tag");
    }

    [Fact]
    public void DeleteCssSelectors_RemovesSelectorFromCssFileAndKeepsOthers()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree => AppendCssToStyle(tree, ".ghost { color: red }"));
        UnusedStyleSelectorsResult found = book.FindUnusedStyleSelectors();

        bool modified = book.DeleteCssSelectors(found.UnusedSelectors);

        modified.Should().BeTrue();
        book.Modified.Should().BeTrue();
        CssResource css = book.GetCssResources().Single();
        css.GetText().Should().NotContain(".ghost");
        css.GetText().Should().Contain("body");
        css.GetText().Should().Contain("h1");
    }

    [Fact]
    public void DeleteCssSelectors_RemovesSelectorFromInlineStyleBlockWithoutTouchingOthers()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree => AddInlineStyleBlockToChapter1(tree, "<style>.ghost2 { color: blue }\nh2 { color: purple }</style>"));

        UnusedStyleSelectorsResult found = book.FindUnusedStyleSelectors();
        found.UnusedSelectors.Select(s => s.SelectorText).Should().Contain(".ghost2");

        bool modified = book.DeleteCssSelectors(
            found.UnusedSelectors.Where(s => s.SelectorText == ".ghost2").ToList());

        modified.Should().BeTrue();
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.GetText().Should().NotContain(".ghost2");
        chapter.GetText().Should().Contain("h2");
    }

    [Fact]
    public void DeleteCssSelectors_WithEmptyList_ReturnsFalseAndDoesNotModify()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        bool modified = book.DeleteCssSelectors(Array.Empty<CssSelectorUsage>());

        modified.Should().BeFalse();
        book.Modified.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    //  Removing unused CSS rules + merging identical selectors/properties
    // -----------------------------------------------------------------

    [Fact]
    public void FindCssCleanupCandidates_FindsSameSelectorMergeGroup()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree => AppendCssToStyle(tree, "body { margin: 1px }"));

        CssCleanupResult result = book.FindCssCleanupCandidates();

        result.Applied.Should().BeTrue();
        result.MergeCandidates.Should().ContainSingle(c => c.Kind == CssMergeKind.SameSelector && c.Description.Contains("body", StringComparison.Ordinal));
    }

    [Fact]
    public void FindCssCleanupCandidates_FindsSamePropertiesMergeGroup()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree => AppendCssToStyle(tree, ".twin1 { color: teal } .twin2 { color: teal }"));

        CssCleanupResult result = book.FindCssCleanupCandidates();

        result.MergeCandidates.Should().ContainSingle(c => c.Kind == CssMergeKind.SameProperties);
    }

    [Fact]
    public void FindCssCleanupCandidates_AbortsWhenAnyHtmlIsNotWellFormed()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });
        HtmlResource chapter = book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");
        chapter.SetText("<html><head><body><p>broken");

        CssCleanupResult result = book.FindCssCleanupCandidates();

        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(chapter);
        result.MergeCandidates.Should().BeEmpty();
        result.UnusedStylesheets.Should().BeEmpty();
    }

    [Fact]
    public void ApplyCssMerges_RewritesCssFileForSelectedCandidatesOnly()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree => AppendCssToStyle(tree, "body { margin: 1px }"));
        CssCleanupResult found = book.FindCssCleanupCandidates();

        bool modified = book.ApplyCssMerges(found.MergeCandidates);

        modified.Should().BeTrue();
        book.Modified.Should().BeTrue();
        CssResource css = book.GetCssResources().Single();
        css.GetText().Should().Contain("margin: 1px");
        new CssInfo(css.GetText()).Rules.Count(r => r.SelectorText == "body").Should().Be(1);
    }

    [Fact]
    public void ApplyCssMerges_WithEmptyList_ReturnsFalseAndDoesNotModify()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        bool modified = book.ApplyCssMerges(Array.Empty<CssMergeCandidate>());

        modified.Should().BeFalse();
        book.Modified.Should().BeFalse();
    }

    [Fact]
    public void FindUnusedStylesheets_FlagsCssFileNotLinkedByAnyHtml()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            string stylesDir = Path.Combine(tree, "EPUB", "styles");
            File.WriteAllText(Path.Combine(stylesDir, "orphan.css"), "body { color: pink }");

            string opfPath = Path.Combine(tree, "EPUB", "package.opf");
            string opf = File.ReadAllText(opfPath);
            opf = opf.Replace(
                "  </manifest>",
                "    <item id=\"orphan-css\" href=\"styles/orphan.css\" media-type=\"text/css\"/>\n  </manifest>",
                StringComparison.Ordinal);
            File.WriteAllText(opfPath, opf);
        });

        IReadOnlyList<CssResource> unused = book.FindUnusedStylesheets();

        unused.Should().ContainSingle(c => c.BookPath.EndsWith("orphan.css", StringComparison.Ordinal));
    }

    [Fact]
    public void DeleteUnreferencedStylesheets_RemovesFromManifestAndDisk()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            string stylesDir = Path.Combine(tree, "EPUB", "styles");
            File.WriteAllText(Path.Combine(stylesDir, "orphan2.css"), "body { color: pink }");

            string opfPath = Path.Combine(tree, "EPUB", "package.opf");
            string opf = File.ReadAllText(opfPath);
            opf = opf.Replace(
                "  </manifest>",
                "    <item id=\"orphan2-css\" href=\"styles/orphan2.css\" media-type=\"text/css\"/>\n  </manifest>",
                StringComparison.Ordinal);
            File.WriteAllText(opfPath, opf);
        });
        CssResource orphan = book.FindUnusedStylesheets().Single();
        string fullPath = orphan.FullPath;

        bool modified = book.DeleteUnreferencedStylesheets(new[] { orphan });

        modified.Should().BeTrue();
        book.Modified.Should().BeTrue();
        book.GetCssResources().Should().NotContain(orphan);
        File.Exists(fullPath).Should().BeFalse();
    }

    [Fact]
    public void DeleteUnreferencedStylesheets_WithEmptyList_ReturnsFalseAndDoesNotModify()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        bool modified = book.DeleteUnreferencedStylesheets(Array.Empty<CssResource>());

        modified.Should().BeFalse();
        book.Modified.Should().BeFalse();
    }
}
