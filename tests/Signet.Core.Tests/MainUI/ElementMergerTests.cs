using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>"Merge Content" — <see cref="ElementMerger"/>.</summary>
public sealed class ElementMergerTests
{
    // The selection is written in the fragment as «…»; the fragment goes into <body>.
    private static (string Text, int Start, int End) Doc(string marked)
    {
        string text = "<html><body>" + marked + "</body></html>";
        int start = text.IndexOf('«', System.StringComparison.Ordinal);
        text = text.Remove(start, 1);
        int end = text.IndexOf('»', System.StringComparison.Ordinal);
        return (text.Remove(end, 1), start, end);
    }

    private static ElementMergeCandidate? Analyze(string marked)
    {
        (string text, int start, int end) = Doc(marked);
        return ElementMerger.Analyze(text, start, end);
    }

    private static string MergedBody(string marked)
    {
        (string text, int start, int end) = Doc(marked);
        FormatEdit edit = ElementMerger.Merge(text, start, end);
        edit.Changed.Should().BeTrue();
        return edit.Text["<html><body>".Length..^"</body></html>".Length];
    }

    [Fact]
    public void Two_paragraphs_separated_by_a_newline_merge_with_a_space()
    {
        Analyze("«<p>Ala</p>\n  <p>kot</p>»").Should().BeEquivalentTo(
            new { ElementName = "p", Count = 2, AttributesDiffer = false });
        MergedBody("«<p>Ala</p>\n  <p>kot</p>»").Should().Be("<p>Ala kot</p>");
    }

    [Fact]
    public void Adjacent_spans_merge_without_a_separator()
    {
        MergedBody("«<span>ab</span><span>cd</span>»").Should().Be("<span>abcd</span>");
    }

    [Fact]
    public void Three_headings_with_nested_markup_merge_and_the_result_is_selected()
    {
        (string text, int start, int end) = Doc("x«<h2 id=\"a\">A <em>b</em></h2> <h2 id=\"a\">C</h2> <h2 id=\"a\">D</h2>»y");

        Analyze("«<h2>1</h2><h2>2</h2><h2>3</h2>»")!.Count.Should().Be(3);
        FormatEdit edit = ElementMerger.Merge(text, start, end);

        const string merged = "<h2 id=\"a\">A <em>b</em> C D</h2>";
        edit.Text.Should().Be("<html><body>x" + merged + "y</body></html>");
        (edit.SelectionStart, edit.SelectionEnd).Should().Be((start, start + merged.Length));
    }

    [Fact]
    public void Whitespace_at_the_edges_of_the_selection_is_ignored()
    {
        Analyze("«\n  <p>a</p>\n<p>b</p>\n»").Should().NotBeNull();
    }

    [Theory]
    [InlineData("«<p>a</p><h1>b</h1>»")]
    [InlineData("«<h1>a</h1><h2>b</h2>»")]
    [InlineData("«<p>a</p><p>b</p><span>c</span>»")]
    public void Elements_of_different_kinds_cannot_be_merged(string marked)
    {
        Analyze(marked).Should().BeNull();
    }

    [Fact]
    public void Plain_whitespace_between_elements_needs_no_warning()
    {
        Analyze("«<p>a</p>\n\n  <p>b</p>»")!.NeedsConfirmation.Should().BeFalse();
    }

    [Theory]
    [InlineData("«<span>abc</span> bcd <span>xyz</span>»", "<span>abc bcd xyz</span>", "bcd")]
    [InlineData("«<p>a</p>\n  tekst\n\n  dalej  <p>b</p>»", "<p>a tekst dalej b</p>", "tekst dalej")]
    [InlineData("«<span>ab</span>cd<span>ef</span>»", "<span>abcdef</span>", "cd")]
    public void Text_between_elements_goes_into_the_merged_element_with_collapsed_whitespace_and_a_warning(
        string marked, string expected, string textBetween)
    {
        ElementMergeCandidate candidate = Analyze(marked)!;

        candidate.TextBetween.Should().Equal(textBetween);
        candidate.NeedsConfirmation.Should().BeTrue();
        MergedBody(marked).Should().Be(expected);
    }

    [Theory]
    [InlineData("«<p>a</p>\n<!-- k -->\n<p>b</p>»", "<p>a b</p>", "<!-- k -->")]
    [InlineData("«<p>a</p><![CDATA[x]]><p>b</p>»", "<p>ab</p>", "<![CDATA[x]]>")]
    [InlineData("«<p>a</p> <?pi x?> <p>b</p>»", "<p>a b</p>", "<?pi x?>")]
    [InlineData("«<span>a</span> t<!--k-->u <span>b</span>»", "<span>a tu b</span>", "<!--k-->")]
    public void Comments_and_other_non_element_nodes_between_elements_are_removed_with_a_warning(
        string marked, string expected, string removed)
    {
        ElementMergeCandidate candidate = Analyze(marked)!;

        candidate.RemovedNodes.Should().Equal(removed);
        candidate.NeedsConfirmation.Should().BeTrue();
        MergedBody(marked).Should().Be(expected);
    }

    [Theory]
    [InlineData("«<p>a</p><br/><p>b</p>»")]
    [InlineData("«<span>a</span> <b>x</b> <span>b</span>»")]
    [InlineData("«<p>a</p> tekst </div><p>b</p>»")]
    public void Another_tag_between_elements_blocks_merging(string marked)
    {
        Analyze(marked).Should().BeNull();
    }

    [Theory]
    [InlineData("<p>«a</p><p>b</p>»", "<p>ab</p>")]
    [InlineData("«<p>a</p><p>b»</p>", "<p>ab</p>")]
    [InlineData("<p>Ala «ma</p>\n<p>kota</p>\n<p>i p»sa</p>", "<p>Ala ma kota i psa</p>")]
    [InlineData("<p cl«ass=\"x\">Ala ma</p>\n<p class=\"x\">kota</p>\n<p class=\"x\">i psa<»/p>", "<p class=\"x\">Ala ma kota i psa</p>")]
    [InlineData("<p>Ala ma</p>«\n  \n<p>kota</p>\n<p>i psa</p>  »\n", "<p>Ala ma</p>\n  \n<p>kota i psa</p>  \n")]
    [InlineData("<p>a <em>x«y</em></p><p><em>z»w</em></p>", "<p>a <em>xy</em><em>zw</em></p>")]
    [InlineData("<div><p>a«</p><p>b»</p></div>", "<div><p>ab</p></div>")]
    [InlineData("<p>a «<b>x</b> <b>y</b>» c</p>", "<p>a <b>x y</b> c</p>")]
    public void Selection_edges_inside_sibling_elements_expand_to_whole_elements(string marked, string expected)
    {
        MergedBody(marked).Should().Be(expected);
    }

    [Fact]
    public void Expanded_merge_selects_the_whole_merged_element()
    {
        (string text, int start, int end) = Doc("x<p>Ala «ma</p><p>ko»ta</p>y");

        FormatEdit edit = ElementMerger.Merge(text, start, end);

        edit.Text[edit.SelectionStart..edit.SelectionEnd].Should().Be("<p>Ala makota</p>");
    }

    [Theory]
    [InlineData("«<p>a</p>»<p>b</p>")]
    [InlineData("<p>«a</p>»<p>b</p>")]
    [InlineData("<p>«a b»</p><p>c</p>")]
    [InlineData("<p>«a <b>x</b> <b>y</b>» c</p>")]
    [InlineData("«<p>a</p> <p>b</p> x»")]
    [InlineData("<p>a</p>\nlu«źny tekst\n<p>b</p>\n<p>c»</p>")]
    [InlineData("<p>a <em>x«y</em> z»</p>")]
    public void Selection_edge_in_loose_text_or_within_a_single_element_cannot_be_merged(string marked)
    {
        Analyze(marked).Should().BeNull();
    }

    [Fact]
    public void Nested_elements_of_the_same_kind_are_merged_only_at_the_selected_level()
    {
        MergedBody("«<div><div>a</div></div><div>b</div>»").Should().Be("<div><div>a</div>b</div>");
    }

    [Theory]
    [InlineData("«<p class=\"a b\" id=\"x\">1</p><p id=\"x\" class=\"b  a\">2</p>»")]
    [InlineData("«<p>1</p><p>2</p>»")]
    public void Same_attributes_in_any_order_and_class_as_a_set_give_no_warning(string marked)
    {
        Analyze(marked)!.AttributesDiffer.Should().BeFalse();
    }

    [Fact]
    public void Different_attributes_are_reported_and_only_the_first_elements_are_kept()
    {
        const string marked = "«<p class=\"a\">1</p> <p class=\"b\">2</p> <p class=\"a\">3</p> <p class=\"a\" id=\"z\">4</p>»";

        ElementMergeCandidate candidate = Analyze(marked)!;

        candidate.AttributesDiffer.Should().BeTrue();
        candidate.FirstOpenTag.Should().Be("<p class=\"a\">");
        candidate.DifferingOpenTags.Should().Equal("<p class=\"b\">", "<p class=\"a\" id=\"z\">");
        MergedBody(marked).Should().Be("<p class=\"a\">1 2 3 4</p>");
    }

    [Fact]
    public void Empty_elements_do_not_add_separators_and_a_self_closing_first_element_gets_a_closing_tag()
    {
        MergedBody("«<p/> <p>a</p> <p></p> <p>b</p>»").Should().Be("<p>a b</p>");
        MergedBody("«<p class=\"c\" /> <p/>»").Should().Be("<p class=\"c\" />");
    }

    [Fact]
    public void Merge_of_an_invalid_selection_leaves_the_text_unchanged_with_a_message()
    {
        (string text, int start, int end) = Doc("«<p>a</p> <b>x</b> <p>b</p>»");

        FormatEdit edit = ElementMerger.Merge(text, start, end);

        edit.Changed.Should().BeFalse();
        edit.Text.Should().Be(text);
        edit.StatusMessage.Should().NotBeNullOrEmpty();
    }
}
