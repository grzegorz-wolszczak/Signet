using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="SpanRemoval"/> — "Remove span" with its consequences for the styling.</summary>
public sealed class SpanRemovalTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // The minimal book with the given CSS and two chapters (chapter2 links the same stylesheet).
    private Book Load(string css, string chapter1Body, string chapter2Body)
    {
        string tree = _temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n" + css + "\n");
        string chapter1 = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        string original = File.ReadAllText(chapter1);
        File.WriteAllText(chapter1, original.Replace("<p>Hello, world.</p>", chapter1Body, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(tree, "EPUB", "text", "chapter2.xhtml"), original.Replace("<p>Hello, world.</p>", chapter2Body, StringComparison.Ordinal));
        string opf = Path.Combine(tree, "EPUB", "package.opf");
        File.WriteAllText(opf, File.ReadAllText(opf)
            .Replace("  </manifest>", "    <item id=\"c2\" href=\"text/chapter2.xhtml\" media-type=\"application/xhtml+xml\"/>\n  </manifest>", StringComparison.Ordinal)
            .Replace("  </spine>", "    <itemref idref=\"c2\"/>\n  </spine>", StringComparison.Ordinal));
        return new ImportEpub(EpubBuilder.BuildInto(tree, _temp, "book.epub")).GetBook();
    }

    private static HtmlResource Chapter(Book book, string name) => book.GetHtmlResources().Single(h => h.Filename == name);

    private static int SpanOffset(string text, string marker = "<span") => text.IndexOf(marker, StringComparison.Ordinal) + 1;

    [Fact]
    public void Removing_a_span_whose_class_has_no_rules_is_safe()
    {
        using Book book = Load(".other { color: red; }", "<p>a <span class=\"x\">b</span></p>", "<p>c</p>");
        HtmlResource html = Chapter(book, "chapter1.xhtml");
        string text = html.GetText();

        SpanRemovalPlan plan = SpanRemoval.Plan(book, html, text, SpanOffset(text), SpanRemovalScope.This)!;

        plan.Consequences.Should().BeEmpty();
        plan.SpanCount.Should().Be(1);
        plan.NewTexts[html.BookPath].Should().Contain("<p>a b</p>");
    }

    [Fact]
    public void Removing_a_span_whose_class_has_rules_lists_what_stops_applying()
    {
        using Book book = Load(".x { color: red; margin: 0; }", "<p>a <span class=\"x\">b</span></p>", "<p>c</p>");
        HtmlResource html = Chapter(book, "chapter1.xhtml");
        string text = html.GetText();

        SpanRemovalPlan plan = SpanRemoval.Plan(book, html, text, SpanOffset(text), SpanRemovalScope.This)!;

        plan.Consequences.Should().ContainSingle().Which.Text.Should().Contain("color").And.Contain(".x");
    }

    [Fact]
    public void A_structural_change_is_a_consequence_even_for_a_bare_span()
    {
        using Book book = Load("p > em { color: blue; }", "<p>a <span>b <em>c</em></span></p>", "<p>c</p>");
        HtmlResource html = Chapter(book, "chapter1.xhtml");
        string text = html.GetText();

        SpanRemovalPlan plan = SpanRemoval.Plan(book, html, text, SpanOffset(text), SpanRemovalScope.This)!;

        plan.Consequences.Should().Contain(c => c.Text.Contains("blue", StringComparison.Ordinal));
    }

    [Fact]
    public void The_book_scope_covers_the_same_spans_of_every_file()
    {
        using Book book = Load(
            ".other { color: red; }",
            "<p><span class=\"x y\">a</span> <span class=\"x\">b</span></p>",
            "<p><span class=\"y x\">c</span> <span class=\"y x\" id=\"keep\">d</span></p>");
        HtmlResource html = Chapter(book, "chapter1.xhtml");
        string text = html.GetText();
        int offset = SpanOffset(text);

        SpanRemoval.CountInBook(book, html, text, offset).Should().Be(2);
        SpanRemovalPlan plan = SpanRemoval.Plan(book, html, text, offset, SpanRemovalScope.Book)!;

        plan.SpanCount.Should().Be(2);
        plan.NewTexts.Should().HaveCount(2);
        plan.NewTexts[html.BookPath].Should().Contain("<p>a <span class=\"x\">b</span></p>");
        plan.NewTexts[Chapter(book, "chapter2.xhtml").BookPath].Should().Contain("<p>c <span class=\"y x\" id=\"keep\">d</span></p>");
        plan.Consequences.Should().BeEmpty();
    }

    [Fact]
    public void There_is_no_plan_for_a_span_with_an_id_or_outside_a_span()
    {
        using Book book = Load(string.Empty, "<p><span id=\"i\">a</span></p>", "<p>c</p>");
        HtmlResource html = Chapter(book, "chapter1.xhtml");
        string text = html.GetText();

        SpanRemoval.Plan(book, html, text, SpanOffset(text), SpanRemovalScope.File).Should().BeNull();
        SpanRemoval.Plan(book, html, text, SpanOffset(text, "<p>"), SpanRemovalScope.File).Should().BeNull();
    }
}
