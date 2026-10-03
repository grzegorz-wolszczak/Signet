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
/// Tests for <see cref="CssCascadeResolver"/> — the cascade/specificity engine behind the Live CSS panel.
/// </summary>
public sealed class CssCascadeResolverTests
{
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

    private static HtmlResource Chapter1(Book book) =>
        book.GetHtmlResources().Single(h => h.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));

    private static int OffsetOfTag(HtmlResource html, string tagFragment) =>
        html.GetText().IndexOf(tagFragment, StringComparison.Ordinal);

    [Fact]
    public void Resolve_matches_a_rule_from_the_linked_stylesheet()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree =>
            {
                AppendCss(tree, ".note { color: red; }");
                SetChapter1Body(tree, "<p class=\"note\">hi</p>");
            });

        HtmlResource html = Chapter1(book);
        int caret = OffsetOfTag(html, "<p class=\"note\">");

        CssCascadeResult? result = CssCascadeResolver.Resolve(html, caret, ResolveCssInfo(book));

        result.Should().NotBeNull();
        result!.MatchedRules.Should().ContainSingle(r => r.SelectorText == ".note");
        result.MatchedRules.Single().Declarations.Single().Property.Should().Be("color");
        result.MatchedRules.Single().Declarations.Single().IsOverridden.Should().BeFalse();
    }

    [Fact]
    public void Resolve_returns_empty_rules_when_nothing_matches()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree =>
            {
                AppendCss(tree, ".other { color: red; }");
                SetChapter1Body(tree, "<p class=\"note\">hi</p>");
            });

        HtmlResource html = Chapter1(book);
        int caret = OffsetOfTag(html, "<p class=\"note\">");

        CssCascadeResult? result = CssCascadeResolver.Resolve(html, caret, ResolveCssInfo(book));

        result.Should().NotBeNull();
        result!.MatchedRules.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_marks_lower_specificity_declaration_as_overridden()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree =>
            {
                AppendCss(tree, ".note { color: red; }\n#special { color: blue; }");
                SetChapter1Body(tree, "<p id=\"special\" class=\"note\">hi</p>");
            });

        HtmlResource html = Chapter1(book);
        int caret = OffsetOfTag(html, "<p id=\"special\"");

        CssCascadeResult? result = CssCascadeResolver.Resolve(html, caret, ResolveCssInfo(book));

        result.Should().NotBeNull();
        CssMatchedRule idRule = result!.MatchedRules.Single(r => r.SelectorText == "#special");
        CssMatchedRule classRule = result.MatchedRules.Single(r => r.SelectorText == ".note");

        idRule.Declarations.Single(d => d.Property == "color").IsOverridden.Should().BeFalse();
        classRule.Declarations.Single(d => d.Property == "color").IsOverridden.Should().BeTrue();
    }

    [Fact]
    public void Resolve_lets_important_beat_higher_specificity()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree =>
            {
                AppendCss(tree, "#special { color: blue; }\n.note { color: red !important; }");
                SetChapter1Body(tree, "<p id=\"special\" class=\"note\">hi</p>");
            });

        HtmlResource html = Chapter1(book);
        int caret = OffsetOfTag(html, "<p id=\"special\"");

        CssCascadeResult? result = CssCascadeResolver.Resolve(html, caret, ResolveCssInfo(book));

        result.Should().NotBeNull();
        CssMatchedRule idRule = result!.MatchedRules.Single(r => r.SelectorText == "#special");
        CssMatchedRule classRule = result.MatchedRules.Single(r => r.SelectorText == ".note");

        classRule.Declarations.Single(d => d.Property == "color").IsOverridden.Should().BeFalse();
        idRule.Declarations.Single(d => d.Property == "color").IsOverridden.Should().BeTrue();
    }

    [Fact]
    public void Resolve_uses_source_order_as_tiebreaker_for_equal_specificity()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree =>
            {
                AppendCss(tree, ".note { color: red; }\n.note { color: green; }");
                SetChapter1Body(tree, "<p class=\"note\">hi</p>");
            });

        HtmlResource html = Chapter1(book);
        int caret = OffsetOfTag(html, "<p class=\"note\">");

        CssCascadeResult? result = CssCascadeResolver.Resolve(html, caret, ResolveCssInfo(book));

        result.Should().NotBeNull();
        result!.MatchedRules.Should().HaveCount(2);
        CssMatchedRule first = result.MatchedRules[0];
        CssMatchedRule second = result.MatchedRules[1];

        first.Declarations.Single().IsOverridden.Should().BeTrue();
        second.Declarations.Single().IsOverridden.Should().BeFalse();
        second.Declarations.Single().Value.Trim().Should().Be("green");
    }

    [Fact]
    public void Resolve_lets_inline_style_beat_any_selector_based_rule_in_the_same_importance_tier()
    {
        // Note: this comparison only holds within the same importance tier — in the CSS cascade the
        // WHOLE "!important" tier (regardless of origin) beats the WHOLE normal tier, so a
        // low-specificity !important rule would win over a non-important inline style (see
        // Resolve_lets_important_beat_higher_specificity). Here both sides are "normal", so the
        // winner is decided by specificity alone — and inline always has the highest.
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(
            temp,
            tree =>
            {
                AppendCss(tree, "#special.note { color: red; }");
                SetChapter1Body(tree, "<p id=\"special\" class=\"note\" style=\"color: green\">hi</p>");
            });

        HtmlResource html = Chapter1(book);
        int caret = OffsetOfTag(html, "<p id=\"special\"");

        CssCascadeResult? result = CssCascadeResolver.Resolve(html, caret, ResolveCssInfo(book));

        result.Should().NotBeNull();
        CssMatchedRule inlineRule = result!.MatchedRules.Single(r => r.IsInlineStyle);
        CssMatchedRule cssRule = result.MatchedRules.Single(r => !r.IsInlineStyle);

        inlineRule.Declarations.Single(d => d.Property == "color").IsOverridden.Should().BeFalse();
        cssRule.Declarations.Single(d => d.Property == "color").IsOverridden.Should().BeTrue();
    }

    [Fact]
    public void Resolve_returns_null_for_a_negative_offset()
    {
        using TempDir temp = new();
        using Book book = LoadModifiedMinimal(temp, _ => { });

        HtmlResource html = Chapter1(book);

        CssCascadeResult? result = CssCascadeResolver.Resolve(html, -1, ResolveCssInfo(book));

        result.Should().BeNull();
    }
}
