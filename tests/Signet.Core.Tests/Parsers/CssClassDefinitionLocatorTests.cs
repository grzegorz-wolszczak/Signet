using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>
/// Tests for <see cref="CssClassDefinitionLocator"/> — "Jump to CSS class definition" (Ctrl+click on
/// <c>class="..."</c>).
/// </summary>
public sealed class CssClassDefinitionLocatorTests
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

    private static void SetChapter1Body(string tree, string bodyHtml)
    {
        string path = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        string text = File.ReadAllText(path);
        int bodyStart = text.IndexOf("<body>", StringComparison.Ordinal) + "<body>".Length;
        int bodyEnd = text.IndexOf("</body>", StringComparison.Ordinal);
        File.WriteAllText(path, text[..bodyStart] + bodyHtml + text[bodyEnd..]);
    }

    private static Func<string, CssInfo?> ResolveCssInfo(Book book)
    {
        FolderKeeper folderKeeper = book.GetFolderKeeper();
        return bookPath => folderKeeper.GetResourceByBookPathNoThrow(bookPath) is CssResource css
            ? new CssInfo(css.GetText())
            : null;
    }

    [Fact]
    public void Find_locates_the_rule_in_a_linked_stylesheet()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree =>
            {
                AppendCss(tree, ".note { color: red }");
                SetChapter1Body(tree, "<p class=\"note\">hi</p>");
            });

        var html = book.GetHtmlResources().Single(h => h.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));

        CssClassDefinition? found = CssClassDefinitionLocator.Find(html, "note", ResolveCssInfo(book));

        found.Should().NotBeNull();
        found!.Value.BookPath.Should().EndWith("style.css");
    }

    [Fact]
    public void Find_locates_the_rule_in_an_inline_style_block_when_no_linked_stylesheet_matches()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree => SetChapter1Body(tree, "<p class=\"note\">hi</p>"));

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        var html = folderKeeper.GetResourceTypeList<HtmlResource>().Single(h => h.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));
        string withInlineStyle = html.GetText().Replace(
            "<meta charset=\"utf-8\"/>",
            "<meta charset=\"utf-8\"/><style>.note { color: blue }</style>",
            StringComparison.Ordinal);
        html.SetText(withInlineStyle);

        CssClassDefinition? found = CssClassDefinitionLocator.Find(html, "note", ResolveCssInfo(book));

        found.Should().NotBeNull();
        found!.Value.BookPath.Should().Be(html.BookPath);
        withInlineStyle.Substring(found.Value.Offset, 5).Should().Be(".note");
    }

    [Fact]
    public void Find_prefers_the_linked_stylesheet_over_an_inline_style_block()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree =>
            {
                AppendCss(tree, ".note { color: red }");
                SetChapter1Body(tree, "<p class=\"note\">hi</p>");
            });

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        var html = folderKeeper.GetResourceTypeList<HtmlResource>().Single(h => h.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));
        html.SetText(html.GetText().Replace(
            "<meta charset=\"utf-8\"/>",
            "<meta charset=\"utf-8\"/><style>.note { color: blue }</style>",
            StringComparison.Ordinal));

        CssClassDefinition? found = CssClassDefinitionLocator.Find(html, "note", ResolveCssInfo(book));

        found!.Value.BookPath.Should().EndWith("style.css");
    }

    [Fact]
    public void Find_returns_null_when_no_rule_matches_the_class()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        var html = book.GetHtmlResources().Single(h => h.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));

        CssClassDefinitionLocator.Find(html, "ghost", ResolveCssInfo(book)).Should().BeNull();
    }

    [Fact]
    public void Find_returns_null_for_an_empty_class_name()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        var html = book.GetHtmlResources().Single(h => h.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));

        CssClassDefinitionLocator.Find(html, string.Empty, ResolveCssInfo(book)).Should().BeNull();
    }
}
