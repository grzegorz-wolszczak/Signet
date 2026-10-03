using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of link completion.</summary>
public sealed class LinkCompletionTests
{
    private static readonly LinkCompletionFile[] Files =
    {
        new("OEBPS/Text/ch1.xhtml", "application/xhtml+xml"),
        new("OEBPS/Text/ch2.xhtml", "application/xhtml+xml"),
        new("OEBPS/Text/ch10.xhtml", "application/xhtml+xml"),
        new("OEBPS/Styles/main style.css", "text/css"),
        new("OEBPS/Images/cover.jpg", "image/jpeg"),
        new("OEBPS/Images/map.svg", "image/svg+xml"),
        new("OEBPS/Fonts/serif.otf", "font/otf"),
        new("OEBPS/content.opf", "application/oebps-package+xml"),
    };

    [Theory]
    [InlineData("<a href=\"ch", LinkTargetKind.TextLink, "ch")]
    [InlineData("<img class=\"x\" src='../Im", LinkTargetKind.Image, "../Im")]
    [InlineData("<link rel=\"stylesheet\" href=\"", LinkTargetKind.Stylesheet, "")]
    [InlineData("<script src=\"a", LinkTargetKind.Any, "a")]
    [InlineData("<svg:image xlink:href=\"c", LinkTargetKind.Any, "c")]
    public void Detects_a_link_attribute_value_being_typed(string typed, LinkTargetKind kind, string query)
    {
        string text = "<body>" + typed;

        LinkCompletionContext? context = LinkCompletion.Detect(text, text.Length, CodeViewSyntax.Html);

        context.Should().NotBeNull();
        context!.Kind.Should().Be(kind);
        context.Query.Should().Be(query);
        context.ReplaceStart.Should().Be(text.Length - query.Length);
        context.AnchorHref.Should().BeNull();
    }

    [Fact]
    public void Hash_switches_to_anchors_of_the_target_document()
    {
        const string text = "<body><a href=\"ch2.xhtml#sec";

        LinkCompletionContext? context = LinkCompletion.Detect(text, text.Length, CodeViewSyntax.Html);

        context.Should().Be(new LinkCompletionContext(text.Length - 3, "sec", LinkTargetKind.TextLink, "ch2.xhtml"));
    }

    [Theory]
    [InlineData("<body><p class=\"x")]
    [InlineData("<body><a href=\"x\">text")]
    [InlineData("<body><a href=\"x\" ")]
    [InlineData("<body></a href=\"x")]
    public void No_context_outside_a_link_attribute_value(string text)
    {
        LinkCompletion.Detect(text, text.Length, CodeViewSyntax.Html).Should().BeNull();
    }

    [Theory]
    [InlineData(CodeViewSyntax.Css, "body {\n  background: url(\"../Im", "../Im")]
    [InlineData(CodeViewSyntax.Css, "@font-face { src: url(", "")]
    [InlineData(CodeViewSyntax.Html, "<style>p { background: url(../", "../")]
    public void Detects_css_url_argument(CodeViewSyntax syntax, string text, string query)
    {
        LinkCompletionContext? context = LinkCompletion.Detect(text, text.Length, syntax);

        context.Should().Be(new LinkCompletionContext(text.Length - query.Length, query, LinkTargetKind.CssResource, null));
    }

    [Fact]
    public void File_candidates_are_relative_hrefs_filtered_by_kind_in_natural_order()
    {
        IReadOnlyList<LinkCompletionItem> text = LinkCompletion.FileCandidates(Files, "OEBPS/Text/ch1.xhtml", LinkTargetKind.TextLink);
        IReadOnlyList<LinkCompletionItem> css = LinkCompletion.FileCandidates(Files, "OEBPS/Styles/main style.css", LinkTargetKind.CssResource);
        IReadOnlyList<LinkCompletionItem> sheets = LinkCompletion.FileCandidates(Files, "OEBPS/Text/ch1.xhtml", LinkTargetKind.Stylesheet);

        text.Select(i => i.Text).Should().Equal("ch2.xhtml", "ch10.xhtml");
        text.Should().OnlyContain(i => i.Description == "Text");
        css.Select(i => i.Text).Should().Equal("../Fonts/serif.otf", "../Images/cover.jpg", "../Images/map.svg");
        sheets.Select(i => i.Text).Should().Equal("../Styles/main%20style.css");
    }

    [Fact]
    public void Anchor_candidates_list_ids_and_names_once_with_a_description()
    {
        const string html = "<body><h1 id=\"top\">Chapter One Title</h1><p id=\"p1\" title=\"First paragraph\">x</p><a name=\"old\"/><span id=\"top\">dup</span></body>";

        IReadOnlyList<LinkCompletionItem> anchors = LinkCompletion.AnchorCandidates(html);

        anchors.Should().Equal(
            new LinkCompletionItem("old", string.Empty),
            new LinkCompletionItem("p1", "First paragraph"),
            new LinkCompletionItem("top", "Chapter One Title"));
    }

    [Fact]
    public void Filter_keeps_subsequence_matches_best_first()
    {
        LinkCompletionItem[] items =
        {
            new("../Images/cover.jpg", "Image"),
            new("../Images/chapter-ornament.png", "Image"),
            new("../Fonts/serif.otf", "Font"),
        };

        IReadOnlyList<LinkCompletionItem> result = LinkCompletion.Filter(items, "co");

        result.Select(i => i.Text).Should().Equal("../Images/cover.jpg", "../Images/chapter-ornament.png");
        LinkCompletion.Filter(items, "xyz").Should().BeEmpty();
        LinkCompletion.Filter(items, string.Empty).Should().Equal(items);
    }

    [Fact]
    public void Score_prefers_matches_at_segment_starts_and_is_case_insensitive()
    {
        LinkCompletion.Score("Images/cover.jpg", "cj").Should().BeGreaterThan(0);
        LinkCompletion.Score("Images/cover.jpg", "IC").Should().BeGreaterThan(0);
        LinkCompletion.Score("abc", "abcd").Should().Be(0);
        LinkCompletion.Score("x/cover", "co").Should().BeGreaterThan(LinkCompletion.Score("xacover", "co") - 1e-12);
        LinkCompletion.Score("a-bc", "b").Should().BeGreaterThan(LinkCompletion.Score("axbc", "b"));
    }
}
