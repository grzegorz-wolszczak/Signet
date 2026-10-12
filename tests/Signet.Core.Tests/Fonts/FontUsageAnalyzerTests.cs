using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Fonts;
using Xunit;

namespace Signet.Core.Tests.Fonts;

/// <summary>Tests of <see cref="FontUsageAnalyzer"/> — which faces the elements ask for and which are missing.</summary>
public sealed class FontUsageAnalyzerTests
{
    private const string CssPath = "OEBPS/Styles/style.css";
    private const string HtmlPath = "OEBPS/Text/ch1.xhtml";

    private const string RegularAndItalic =
        "@font-face { font-family: K; src: url(../Fonts/r.ttf) }\n"
        + "@font-face { font-family: K; font-style: italic; src: url(../Fonts/i.ttf) }\n";

    private static FontUsageReport Analyze(string css, string body)
    {
        string html = "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body>" + body + "</body></html>";
        BookFontIndex index = BookFontIndex.Build(new[] { new FontIndexSource(CssPath, css, true) });
        return FontUsageAnalyzer.Analyze(
            new[] { (HtmlPath, html, (IReadOnlyList<string>)new[] { CssPath }) },
            path => path == CssPath ? css : null,
            index);
    }

    [Fact]
    public void Bold_text_without_a_bold_face_is_reported()
    {
        FontUsageReport report = Analyze(
            RegularAndItalic + "body { font-family: K, serif }\n.x { font-weight: 700 }",
            "<p>plain</p><p><b>bold</b> and <i>italic</i></p><p class=\"x\"><em>both</em></p>");

        report.ElementsByFamily["k"].Should().Be(5);
        report.Missing.Select(m => (m.Variant, m.Elements)).Should().Equal(("bold", 1), ("bold italic", 1));
        report.Missing[0].BookPath.Should().Be(HtmlPath);
        report.Missing[0].Offset.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Text_in_another_family_is_not_counted()
    {
        FontUsageReport report = Analyze(
            RegularAndItalic + "body { font-family: K, serif }\nh1 { font: bold 2em Georgia, serif }",
            "<h1>title</h1><p style=\"font-family: serif\">p</p>");

        report.ElementsByFamily.Should().BeEmpty();
        report.Missing.Should().BeEmpty();
    }

    [Fact]
    public void Inline_styles_and_important_follow_the_cascade()
    {
        FontUsageReport report = Analyze(
            RegularAndItalic + "p { font-family: K !important; font-weight: bold }\n#a { font-family: Georgia }",
            "<p id=\"a\" style=\"font-weight: normal\">a</p>");

        report.ElementsByFamily["k"].Should().Be(1);
        report.Missing.Should().BeEmpty();
    }

    [Theory]
    [InlineData(700, "normal", "bold")]
    [InlineData(400, "italic", null)]
    [InlineData(700, "italic", "bold italic")]
    [InlineData(400, "oblique", null)]
    [InlineData(300, "normal", null)]
    public void MissingVariant_follows_the_font_matching(int weight, string style, string? expected)
    {
        IReadOnlyList<FontFaceRule> faces = BookFontIndex.Build(new[] { new FontIndexSource(CssPath, RegularAndItalic, true) }).Rules;

        FontUsageAnalyzer.MissingVariant(faces, weight, style).Should().Be(expected);
    }

    [Fact]
    public void MissingVariant_accepts_a_variable_weight_range()
    {
        IReadOnlyList<FontFaceRule> faces = BookFontIndex.Build(new[]
        {
            new FontIndexSource(CssPath, "@font-face { font-family: V; font-weight: 100 900; src: url(v.ttf) }", true),
        }).Rules;

        FontUsageAnalyzer.MissingVariant(faces, 700, "normal").Should().BeNull();
        FontUsageAnalyzer.MissingVariant(faces, 700, "italic").Should().Be("bold italic");
        FontUsageAnalyzer.MissingVariant(faces, 400, "italic").Should().Be("italic");
    }
}
