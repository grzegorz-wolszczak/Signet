using AwesomeAssertions;
using Signet.Core.Footnotes;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>Tests of <see cref="FootnoteStyles"/> — keeping the rules of Signet's footnote classes in a stylesheet.</summary>
public sealed class FootnoteStylesTests
{
    private static readonly FootnoteStyles Styles = new("a: 1;", "b: 2;\n  c: 3;", "d: 4;", "e: 5;", "f: 6;");

    [Fact]
    public void Missing_rules_are_appended_one_declaration_per_line()
    {
        Styles.ApplyTo("p { margin: 0; }").Should().Be(
            "p { margin: 0; }\n\n.signet-fn-ref {\n  a: 1;\n}\n"
            + "\n.signet-fn-separator {\n  f: 6;\n}\n"
            + "\n.signet-footnotes {\n  d: 4;\n}\n"
            + "\n.signet-endnotes {\n  e: 5;\n}\n"
            + "\n.signet-fn {\n  b: 2;\n  c: 3;\n}\n");
    }

    [Fact]
    public void A_different_rule_is_rewritten_and_the_rest_of_the_stylesheet_stays()
    {
        string css = Styles.ApplyTo("h1 { x: y; }\n");
        string edited = css.Replace("a: 1;", "color: red;", System.StringComparison.Ordinal) + "em { z: w; }\n";

        string result = Styles.ApplyTo(edited);

        result.Should().Be(css + "em { z: w; }\n");
        Styles.ApplyTo(result).Should().Be(result, "applying the same definitions again changes nothing");
    }

    [Fact]
    public void Only_a_top_level_rule_with_exactly_the_class_is_the_rule_of_the_class()
    {
        const string css = "@media print {\n  .signet-fn { x: 1; }\n}\n.signet-fn, p { y: 2; }\n";

        string result = Styles.ApplyTo(css);

        result.Should().StartWith(css).And.Contain("\n.signet-fn {\n  b: 2;\n  c: 3;\n}\n");
    }

    [Fact]
    public void Empty_and_invalid_definitions_leave_their_rules_alone()
    {
        FootnoteStyles styles = new(string.Empty, "x: 1; }", "  \n ", "/* y */", "f: 6;");

        styles.ApplyTo(".signet-fn { old: 1; }\n").Should().Be(".signet-fn { old: 1; }\n\n.signet-fn-separator {\n  f: 6;\n}\n");
        FootnoteStyles.IsValidDeclarations("x: 1;").Should().BeTrue();
        FootnoteStyles.IsValidDeclarations("x: 1; }").Should().BeFalse();
    }

    [Fact]
    public void Windows_line_endings_are_kept()
    {
        new FootnoteStyles("a: 1;", "", "", "", "").ApplyTo("p { m: 0; }\r\n")
            .Should().Be("p { m: 0; }\r\n\r\n.signet-fn-ref {\r\n  a: 1;\r\n}\r\n");
    }
}
