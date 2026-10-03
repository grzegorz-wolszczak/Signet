using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>Tests for <see cref="CssSelectorUsageAnalyzer"/> — "Delete Unused Stylesheet Selectors".</summary>
public sealed class CssSelectorUsageAnalyzerTests
{
    /// <summary>Copies <c>epub3/minimal</c> to a temp folder, lets the caller modify the tree, then builds and loads it.</summary>
    private static Book LoadModifiedMinimal(TempDir temp, Action<string> mutate)
    {
        string tree = temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        mutate(tree);
        string epub = EpubBuilder.BuildInto(tree, temp, "book.epub");
        return new ImportEpub(epub).GetBook();
    }

    private static void AppendCss(string tree, string css)
    {
        string path = Path.Combine(tree, "EPUB", "styles", "style.css");
        File.WriteAllText(path, File.ReadAllText(path) + "\n" + css + "\n");
    }

    [Fact]
    public void GetUnusedSelectors_FlagsSelectorNotMatchedByAnyLinkedXhtml()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree => AppendCss(tree, ".ghost { color: red }"));

        var unused = CssSelectorUsageAnalyzer.GetUnusedSelectors(book);

        unused.Select(u => u.SelectorText).Should().Equal(".ghost");
        unused.Single().CssBookPath.Should().EndWith("style.css");
    }

    [Fact]
    public void GetUnusedSelectors_DoesNotFlagSelectorsActuallyUsed()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        // The base style.css has only `body` and `h1` — both used in chapter1.xhtml.
        CssSelectorUsageAnalyzer.GetUnusedSelectors(book).Should().BeEmpty();
    }

    [Fact]
    public void GetSelectorUsage_ReportsWhereEachSelectorIsUsed()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree => AppendCss(tree, ".ghost { color: red }"));

        var usage = CssSelectorUsageAnalyzer.GetSelectorUsage(book);

        usage.Single(u => u.SelectorText == "body").UsedInHtmlBookPath.Should().EndWith("chapter1.xhtml");
        usage.Single(u => u.SelectorText == "body").IsUsed.Should().BeTrue();
        usage.Single(u => u.SelectorText == ".ghost").IsUsed.Should().BeFalse();
    }

    [Fact]
    public void GetUnusedSelectors_KeepsMediaOverlayActiveClassSelectors()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            AppendCss(tree, ".mo-active { color: red }\n.ghost { color: blue }");

            string opfPath = Path.Combine(tree, "EPUB", "package.opf");
            string opf = File.ReadAllText(opfPath).Replace(
                "<meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta>",
                "<meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta>\n"
                + "    <meta property=\"media:active-class\">mo-active</meta>",
                StringComparison.Ordinal);
            File.WriteAllText(opfPath, opf);
        });

        var unused = CssSelectorUsageAnalyzer.GetUnusedSelectors(book);

        unused.Select(u => u.SelectorText).Should().Equal(".ghost");
    }

    [Fact]
    public void GetUnusedSelectors_ConsidersInternalStyleBlocks()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree =>
        {
            string chapter = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
            File.WriteAllText(chapter, File.ReadAllText(chapter).Replace(
                "</head>",
                "  <style type=\"text/css\">.inline-used { color: red } .inline-ghost { color: blue }</style>\n</head>",
                StringComparison.Ordinal).Replace(
                "<p>Hello, world.</p>",
                "<p class=\"inline-used\">Hello, world.</p>",
                StringComparison.Ordinal));
        });

        var unused = CssSelectorUsageAnalyzer.GetUnusedSelectors(book);

        unused.Select(u => u.SelectorText).Should().Equal(".inline-ghost");
        unused.Single().CssBookPath.Should().EndWith("chapter1.xhtml");
    }

    [Fact]
    public void GetSelectorUsage_TreatsUnparseableSelectorAsUsed()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, tree => AppendCss(tree, "!!! broken ??? { color: red }"));

        var usage = CssSelectorUsageAnalyzer.GetSelectorUsage(book);

        usage.Where(u => !u.SelectorText.StartsWith('.') && u.SelectorText is not ("body" or "h1"))
            .Should().OnlyContain(u => u.IsUsed);
    }

    [Fact]
    public void GetUnusedSelectors_MediaCorpus_HasNoUnusedSelectors()
    {
        using TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        using Book book = new ImportEpub(epub).GetBook();

        CssSelectorUsageAnalyzer.GetUnusedSelectors(book).Should().BeEmpty();
    }
}
