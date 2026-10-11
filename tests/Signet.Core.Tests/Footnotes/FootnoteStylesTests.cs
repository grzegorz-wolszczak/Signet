using AwesomeAssertions;
using Signet.Core.Footnotes;
using Xunit;

namespace Signet.Core.Tests.Footnotes;

/// <summary>Tests of <see cref="FootnoteStyles"/> — keeping the rules of Signet's footnote classes in a stylesheet.</summary>
public sealed class FootnoteStylesTests
{
    private static readonly FootnoteStyles Styles = new("top: 1px;", "color: red;\n  margin: 0;", "text-indent: 0;", "width: 4em;", "height: 5em;", "border: none;", "padding: 1px;", "margin: 2px;");

    [Fact]
    public void Missing_rules_are_appended_one_declaration_per_line()
    {
        Styles.ApplyTo("p { margin: 0; }").Should().Be(
            "p { margin: 0; }\n\n.signet-fn-ref {\n  top: 1px;\n}\n"
            + "\n.signet-fn-separator {\n  border: none;\n}\n"
            + "\n.signet-footnotes {\n  width: 4em;\n}\n"
            + "\n.signet-endnotes {\n  height: 5em;\n}\n"
            + "\n.signet-fn {\n  color: red;\n  margin: 0;\n}\n"
            + "\n.signet-fn-p {\n  text-indent: 0;\n}\n"
            + "\n.signet-fn-group {\n  padding: 1px;\n}\n"
            + "\n.signet-fn-group-title {\n  margin: 2px;\n}\n");
    }

    [Fact]
    public void A_different_rule_is_rewritten_and_the_rest_of_the_stylesheet_stays()
    {
        string css = Styles.ApplyTo("h1 { x: y; }\n");
        string edited = css.Replace("top: 1px;", "color: blue;", System.StringComparison.Ordinal) + "em { z: w; }\n";

        string result = Styles.ApplyTo(edited);

        result.Should().Be(css + "em { z: w; }\n");
        Styles.ApplyTo(result).Should().Be(result, "applying the same definitions again changes nothing");
    }

    [Fact]
    public void Only_a_top_level_rule_with_exactly_the_class_is_the_rule_of_the_class()
    {
        const string css = "@media print {\n  .signet-fn { x: 1; }\n}\n.signet-fn, p { y: 2; }\n";

        string result = Styles.ApplyTo(css);

        result.Should().StartWith(css).And.Contain("\n.signet-fn {\n  color: red;\n  margin: 0;\n}\n");
    }

    [Fact]
    public void Empty_and_invalid_definitions_leave_their_rules_alone()
    {
        FootnoteStyles styles = new(string.Empty, "color: 1; }", string.Empty, "  \n ", "/* y */", "border: none;", string.Empty, string.Empty);

        styles.ApplyTo(".signet-fn { old: 1; }\n").Should().Be(".signet-fn { old: 1; }\n\n.signet-fn-separator {\n  border: none;\n}\n");
        FootnoteStyles.IsValidDeclarations("x: 1;").Should().BeTrue();
        FootnoteStyles.IsValidDeclarations("x: 1; }").Should().BeFalse();
    }

    [Theory]
    [InlineData("color: red;\nmargin: 0", null, "")]
    [InlineData("font-size: 1em !important;\n-webkit-hyphens: auto;\n--gap: 2px;", null, "")]
    [InlineData("background: url(\"a;b.png\");", null, "")]
    [InlineData("", null, "")]
    [InlineData("color: red; }", FootnoteStyleProblem.Brace, "}")]
    [InlineData("color: red; /* x */", FootnoteStyleProblem.Comment, "/* */")]
    [InlineData("color red;", FootnoteStyleProblem.MissingColon, "color red")]
    [InlineData("color: red\nmargin: 0;", FootnoteStyleProblem.MissingSemicolon, "color: red")]
    [InlineData("1color: red;", FootnoteStyleProblem.InvalidProperty, "1color")]
    [InlineData("fnot-size: 1em;", FootnoteStyleProblem.UnknownProperty, "fnot-size")]
    [InlineData("color: ;", FootnoteStyleProblem.EmptyValue, "color")]
    [InlineData("color: !important;", FootnoteStyleProblem.EmptyValue, "color")]
    public void Validate_finds_the_first_problem_of_a_definition(string declarations, FootnoteStyleProblem? problem, string detail)
    {
        FootnoteStyleError? error = FootnoteStyles.Validate(declarations);

        if (problem is null)
        {
            error.Should().BeNull();
            FootnoteStyles.IsValidDeclarations(declarations).Should().BeTrue();
        }
        else
        {
            error.Should().Be(new FootnoteStyleError(problem.Value, detail));
            FootnoteStyles.IsValidDeclarations(declarations).Should().BeFalse();
        }
    }

    [Fact]
    public void An_invalid_definition_is_not_written_to_the_stylesheet()
    {
        new FootnoteStyles("fnot-size: 1em;", "", "", "", "", "", "", "").ApplyTo(".signet-fn-ref { old: 1; }\n")
            .Should().Be(".signet-fn-ref { old: 1; }\n");
    }

    [Fact]
    public void Windows_line_endings_are_kept()
    {
        new FootnoteStyles("top: 1px;", "", "", "", "", "", "", "").ApplyTo("p { m: 0; }\r\n")
            .Should().Be("p { m: 0; }\r\n\r\n.signet-fn-ref {\r\n  top: 1px;\r\n}\r\n");
    }
}
