using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>Tests of <see cref="CssDeclarationScanner"/> — rules and declarations with exact offsets.</summary>
public sealed class CssDeclarationScannerTests
{
    [Fact]
    public void ScanRules_reports_rules_and_declarations_with_offsets_into_the_original_text()
    {
        const string css = "/* fonts */\r\n@font-face {\r\n  font-family: \"A;B\";\r\n  src: url(a.ttf)\r\n}\r\np { color: red; }";

        IReadOnlyList<CssScannedRule> rules = CssDeclarationScanner.ScanRules(css);

        rules.Should().HaveCount(2);
        CssScannedRule fontFace = rules[0];
        fontFace.IsFontFace.Should().BeTrue();
        css[fontFace.Start..fontFace.End].Should().StartWith("@font-face").And.EndWith("}");
        fontFace.Declarations.Select(d => d.Property).Should().Equal("font-family", "src");
        CssScannedDeclaration family = fontFace.Declarations[0];
        css[family.ValueStart..family.ValueEnd].Should().Be("\"A;B\"");
        css[family.Start..family.End].Should().Be("font-family: \"A;B\";");
        CssScannedDeclaration src = fontFace.Declarations[1];
        css[src.ValueStart..src.ValueEnd].Should().Be("url(a.ttf)");
        src.End.Should().Be(src.ValueEnd, "the last declaration has no semicolon");
        rules[1].Prelude.Should().Be("p");
    }

    [Fact]
    public void ScanRules_descends_into_grouping_rules()
    {
        const string css = "@import url(x.css);\n@media screen { h1 { font-family: serif } @font-face { font-family: X } }\n@page { margin: 0 }";

        IReadOnlyList<CssScannedRule> rules = CssDeclarationScanner.ScanRules(css);

        rules.Select(r => r.Prelude).Should().Equal("h1", "@font-face", "@page");
        rules[0].ParentAtRule.Should().Be("@media screen");
        rules[1].ParentAtRule.Should().Be("@media screen");
        rules[2].ParentAtRule.Should().BeNull();
    }

    [Fact]
    public void ScanDeclarations_skips_comments_and_keeps_important()
    {
        const string style = " /* x */ font-family: 'Foo', serif !important ; color:blue";

        IReadOnlyList<CssScannedDeclaration> declarations = CssDeclarationScanner.ScanDeclarations(style, 0, style.Length);

        declarations.Select(d => style[d.ValueStart..d.ValueEnd]).Should().Equal("'Foo', serif !important", "blue");
    }
}
