using System.Linq;
using AwesomeAssertions;
using Signet.Core.Fonts;
using Xunit;

namespace Signet.Core.Tests.Fonts;

/// <summary>Tests of <see cref="BookFontIndex"/> — <c>@font-face</c> rules and family usages across the book.</summary>
public sealed class BookFontIndexTests
{
    private const string CssPath = "OEBPS/Styles/style.css";
    private const string HtmlPath = "OEBPS/Text/ch1.xhtml";

    [Fact]
    public void Build_reads_font_face_rules_and_resolves_their_sources()
    {
        const string css = "@font-face {\n  font-family: \"KCaecilia\";\n  font-weight: bold;\n  font-style: italic;\n"
            + "  src: local(\"KCaecilia\"), url(\"../Fonts/K%20BI.ttf\") format(\"truetype\"), url(http://x/y.ttf);\n}\n";

        BookFontIndex index = BookFontIndex.Build(new[] { new FontIndexSource(CssPath, css, true) });

        FontFaceRule rule = index.Rules.Should().ContainSingle().Subject;
        rule.Family.Should().Be("KCaecilia");
        rule.Weight.Should().Be(new FontWeightRange(700, 700));
        rule.Style.Should().Be("italic");
        rule.Sources.Select(s => s.BookPath).Should().Equal(null, "OEBPS/Fonts/K BI.ttf", null);
        rule.Sources[0].IsLocal.Should().BeTrue();
        css[rule.Start..rule.End].Should().StartWith("@font-face").And.EndWith("}");
        index.RulesForFont("OEBPS/Fonts/K BI.ttf").Should().ContainSingle();
        index.RulesForFamily("kcaecilia").Should().ContainSingle();
    }

    [Fact]
    public void Build_finds_usages_in_stylesheets_style_blocks_and_style_attributes()
    {
        const string css = "body { font-family: \"KCaecilia\", serif; }\nh1 { font: bold 2em/1 KCaecilia, sans-serif }";
        const string html = "<html><head><style>p.x { font-family: 'Georgia' }</style></head>"
            + "<body><p style=\"color: red; font-family: &quot;KCaecilia&quot;, serif\">a</p></body></html>";

        BookFontIndex index = BookFontIndex.Build(new[]
        {
            new FontIndexSource(CssPath, css, true),
            new FontIndexSource(HtmlPath, html, false),
        });

        index.Usages.Select(u => (u.Context, u.Property)).Should().Equal(
            ("body", "font-family"), ("h1", "font"), ("p.x", "font-family"), ("style=\"color: red; font-family: \"KCaecilia\", serif\"", "font-family"));
        index.UsagesOfFamily("KCaecilia").Should().HaveCount(3);
        index.Usages[1].ShorthandWeight.Should().Be("bold");

        FontFamilyUsage attribute = index.Usages[3];
        attribute.Kind.Should().Be(FontStyleSourceKind.StyleAttribute);
        html[attribute.Families[0].Start..attribute.Families[0].End].Should().Be("&quot;KCaecilia&quot;");
        html[attribute.AttributeStart..attribute.AttributeEnd].Should().Be(" style=\"color: red; font-family: &quot;KCaecilia&quot;, serif\"");
    }

    [Fact]
    public void Build_ignores_font_family_inside_font_face_as_a_usage()
    {
        BookFontIndex index = BookFontIndex.Build(new[]
        {
            new FontIndexSource(CssPath, "@font-face { font-family: A; src: url(a.ttf) }", true),
        });

        index.Usages.Should().BeEmpty();
        index.Rules.Single().Sources.Single().BookPath.Should().Be("OEBPS/Styles/a.ttf");
    }
}
