using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="CodeViewModel"/> — the working copy of the content, modification, saving, well-formedness, the split marker.</summary>
public sealed class CodeViewModelTests
{
    private const string ValidXhtml =
        "<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head><title>t</title></head>\n<body><p>hi</p></body>\n</html>\n";

    private const string BrokenXhtml =
        "<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head><title>t</title></head>\n<body><p>oops</body>\n</html>\n";

    private static HtmlResource NewHtml(TempDir temp, string text)
    {
        var resource = new HtmlResource(temp.Path, temp.Combine("ch.xhtml"));
        resource.SetText(text);
        return resource;
    }

    [Fact]
    public void Loads_resource_text_and_starts_unmodified()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, ValidXhtml));

        model.Text.Should().Be(ValidXhtml);
        model.IsModified.Should().BeFalse();
        model.Syntax.Should().Be(CodeViewSyntax.Html);
    }

    [Fact]
    public void Editing_text_marks_modified_until_saved_to_resource()
    {
        using TempDir temp = new();
        HtmlResource resource = NewHtml(temp, ValidXhtml);
        var model = new CodeViewModel(resource);

        model.Text = ValidXhtml.Replace("hi", "bye");
        model.IsModified.Should().BeTrue();

        model.SaveToResource().Should().BeTrue();
        model.IsModified.Should().BeFalse();
        resource.GetText().Should().Contain("bye");
    }

    [Fact]
    public void SaveToResource_is_a_no_op_when_nothing_changed()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, ValidXhtml));

        model.SaveToResource().Should().BeFalse();
    }

    [Fact]
    public void ReloadFromResource_discards_unsaved_edits()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, ValidXhtml));
        model.Text = "totally different";

        model.ReloadFromResource();

        model.Text.Should().Be(ValidXhtml);
        model.IsModified.Should().BeFalse();
    }

    [Fact]
    public void CheckWellFormed_passes_for_valid_xhtml()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, ValidXhtml));

        WellFormedResult? result = model.CheckWellFormed();

        result.Should().NotBeNull();
        result!.IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void CheckWellFormed_reports_position_for_broken_xhtml()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, BrokenXhtml));

        WellFormedResult? result = model.CheckWellFormed();

        result.Should().NotBeNull();
        result!.IsWellFormed.Should().BeFalse();
        result.Line.Should().BeGreaterThan(0);
        result.Column.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CheckWellFormed_checks_css_structure_for_css_resources()
    {
        using TempDir temp = new();
        var resource = new CssResource(temp.Path, temp.Combine("s.css"));
        resource.SetText("body { color: red; }");
        var model = new CodeViewModel(resource);

        model.SupportsWellFormedCheck.Should().BeTrue();
        model.CheckWellFormed()!.IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void CheckWellFormed_reports_position_for_broken_css()
    {
        using TempDir temp = new();
        var resource = new CssResource(temp.Path, temp.Combine("s.css"));
        resource.SetText("body { color: red; }\np {");
        var model = new CodeViewModel(resource);

        WellFormedResult? result = model.CheckWellFormed();

        result.Should().NotBeNull();
        result!.IsWellFormed.Should().BeFalse();
        result.Line.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetCssRuleCount_counts_rules_for_css_and_zero_otherwise()
    {
        using TempDir temp = new();
        var css = new CssResource(temp.Path, temp.Combine("s.css"));
        css.SetText("body { color: red; }\nh1 { font-size: 2em; }\np { margin: 0; }");
        new CodeViewModel(css).GetCssRuleCount().Should().Be(3);

        var html = NewHtml(temp, ValidXhtml);
        new CodeViewModel(html).GetCssRuleCount().Should().Be(0);
    }

    [Fact]
    public void CheckWellFormed_uses_plain_xml_check_for_xml_resources()
    {
        using TempDir temp = new();
        var resource = new XmlResource(temp.Path, temp.Combine("data.xml"));
        resource.SetText("<root><a></root>");
        var model = new CodeViewModel(resource);

        model.Syntax.Should().Be(CodeViewSyntax.Xml);
        model.CheckWellFormed()!.IsWellFormed.Should().BeFalse();
    }

    [Fact]
    public void InsertSectionMarkerAt_inserts_marker_and_returns_caret_past_it()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "<body>AB</body>"));
        int at = "<body>A".Length;

        int caret = model.InsertSectionMarkerAt(at);

        model.Text.Should().Be("<body>A" + CodeViewModel.SectionMarker + "B</body>");
        caret.Should().Be(at + CodeViewModel.SectionMarker.Length);
    }

    [Fact]
    public void InsertSectionMarkerAt_clamps_out_of_range_offsets()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "abc"));

        model.InsertSectionMarkerAt(999);

        model.Text.Should().Be("abc" + CodeViewModel.SectionMarker);
    }

    [Fact]
    public void GetTagPairHighlight_marks_open_and_matching_close_when_caret_is_in_the_open_tag()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "<body><p>hi</p></body>"));

        TagPairHighlight? pair = model.GetTagPairHighlight("<body><".Length); // inside <p>

        pair.Should().NotBeNull();
        pair!.Value.Open.Should().Be((6, 3));
        pair.Value.Close.Should().Be((11, 4));
    }

    [Fact]
    public void GetTagPairHighlight_marks_the_pair_when_caret_is_in_the_close_tag()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "<body><p>hi</p></body>"));

        TagPairHighlight? pair = model.GetTagPairHighlight("<body><p>hi</".Length);

        pair!.Value.Open.Should().Be((6, 3));
        pair.Value.Close.Should().Be((11, 4));
    }

    [Fact]
    public void GetTagPairHighlight_is_null_when_caret_is_in_text_content()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "<body><p>hi</p></body>"));

        model.GetTagPairHighlight("<body><p>h".Length).Should().BeNull();
    }

    [Fact]
    public void GetTagPairHighlight_has_no_close_range_for_a_self_closing_tag()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "<body><br/>x</body>"));

        TagPairHighlight? pair = model.GetTagPairHighlight("<body><b".Length);

        pair!.Value.Open.Should().Be((6, 5));
        pair.Value.Close.Should().BeNull();
    }

    [Fact]
    public void GetTagPairHighlight_is_null_for_css()
    {
        using TempDir temp = new();
        var resource = new CssResource(temp.Path, temp.Combine("s.css"));
        resource.SetText("body { color: red; }");
        var model = new CodeViewModel(resource);

        model.GetTagPairHighlight(2).Should().BeNull();
    }

    [Theory]
    [InlineData("<body><a href=\"ch2.xhtml#n1\">x</a></body>", "<body><a href=\"ch", "ch2.xhtml#n1")]
    [InlineData("<body><img src=\"../Images/a.png\"/></body>", "<body><img src=\"", "../Images/a.png")]
    [InlineData("<body><a href=\"ch2.xhtml\">x</a></body>", "<body><a hr", null)]
    [InlineData("<body><a href=\"ch2.xhtml\">x</a></body>", "<body><a href=\"ch2.xhtml\">", null)]
    [InlineData("<style>p { background: url('../Images/b.png') }</style>", "<style>p { background: url('../I", "../Images/b.png")]
    public void GetLinkAtCaret_returns_the_link_value_under_the_caret(string html, string before, string? expected)
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, html));

        model.GetLinkAtCaret(before.Length).Should().Be(expected);
    }

    [Theory]
    [InlineData("<body><img src=\"../Images/a.png\" alt=\"x\"/></body>", "<body><img s", "../Images/a.png")]
    [InlineData("<body><img alt=\"x\" src=\"../Images/a.png\"/></body>", "<body><img al", "../Images/a.png")]
    [InlineData("<body><svg><image xlink:href=\"../Images/c.jpg\"/></svg></body>", "<body><svg><image x", "../Images/c.jpg")]
    [InlineData("<body><a href=\"ch2.xhtml\">x</a></body>", "<body><a h", null)]
    [InlineData("<body><img src=\"../Images/a.png\"/>text</body>", "<body><img src=\"../Images/a.png\"/>te", null)]
    public void GetImageSourceAtCaret_returns_the_image_reference_of_the_tag_under_the_caret(string html, string before, string? expected)
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, html));

        model.GetImageSourceAtCaret(before.Length).Should().Be(expected);
    }

    [Fact]
    public void GetLinkAtCaret_finds_url_in_a_stylesheet()
    {
        using TempDir temp = new();
        var resource = new CssResource(temp.Path, temp.Combine("s.css"));
        resource.SetText("body {\n  background: url(\"../Images/a%20b.png\");\n}");
        var model = new CodeViewModel(resource);

        model.GetLinkAtCaret("body {\n  background: url(\"../Im".Length).Should().Be("../Images/a%20b.png");
        model.GetLinkAtCaret(2).Should().BeNull();
    }

    [Fact]
    public void GetClassNameAtCaret_returns_the_single_class_under_the_caret()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "<body><p class=\"note\">hi</p></body>"));

        model.GetClassNameAtCaret("<body><p class=\"no".Length).Should().Be("note");
    }

    [Fact]
    public void GetClassNameAtCaret_picks_the_token_under_the_caret_among_several_classes()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "<body><p class=\"a bold c\">hi</p></body>"));

        model.GetClassNameAtCaret("<body><p class=\"a bo".Length).Should().Be("bold");
    }

    [Fact]
    public void GetClassNameAtCaret_is_null_outside_the_class_attribute_value()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "<body><p class=\"note\">hi</p></body>"));

        model.GetClassNameAtCaret("<body><p ".Length).Should().BeNull();
        model.GetClassNameAtCaret("<body><p class=\"note\">h".Length).Should().BeNull();
    }

    [Fact]
    public void GetClassNameAtCaret_is_null_when_the_tag_has_no_class_attribute()
    {
        using TempDir temp = new();
        var model = new CodeViewModel(NewHtml(temp, "<body><p>hi</p></body>"));

        model.GetClassNameAtCaret("<body><p".Length).Should().BeNull();
    }

    [Fact]
    public void GetClassNameAtCaret_is_null_for_css()
    {
        using TempDir temp = new();
        var resource = new CssResource(temp.Path, temp.Combine("s.css"));
        resource.SetText(".note { color: red; }");
        var model = new CodeViewModel(resource);

        model.GetClassNameAtCaret(2).Should().BeNull();
    }

    [Fact]
    public void FormatCaretPosition_includes_codepoint_name_and_hex_like_original()
    {
        CodeViewModel.FormatCaretPosition(12, 5, 'a')
            .Should().Be("LATIN SMALL LETTER A (U+0061) — Linia 12, kol. 5");
    }

    [Fact]
    public void FormatCaretPosition_omits_the_codepoint_at_end_of_document() =>
        CodeViewModel.FormatCaretPosition(3, 1).Should().Be("EOF — Linia 3, kol. 1");
}
