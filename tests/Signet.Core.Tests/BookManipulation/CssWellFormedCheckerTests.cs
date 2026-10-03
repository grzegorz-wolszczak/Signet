using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;
using Signet.Core.Localization;
using Signet.Core.Tests.TestSupport;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="CssWellFormedChecker"/> (checking the CSS structure).</summary>
public sealed class CssWellFormedCheckerTests
{
    [Fact]
    public void Check_passes_for_structurally_valid_css()
    {
        WellFormedResult result = CssWellFormedChecker.Check(
            "body { color: red; }\n\nh1 { font-size: 1.5em; }\n");

        result.IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void Check_passes_for_empty_css()
    {
        CssWellFormedChecker.Check(string.Empty).IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void Check_reports_unexpected_closing_brace_with_position()
    {
        WellFormedResult result = CssWellFormedChecker.Check("body { color: red; }\n}\n");

        result.IsWellFormed.Should().BeFalse();
        result.Line.Should().Be(2);
        result.Column.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Check_reports_unterminated_block_at_end_of_document()
    {
        WellFormedResult result = CssWellFormedChecker.Check("body {\n  color: red;\n");

        result.IsWellFormed.Should().BeFalse();
        result.Message.Should().Be(CoreStrings.Get("Css_UnterminatedBlock"));
    }

    [Fact]
    public void Check_reports_unterminated_comment()
    {
        WellFormedResult result = CssWellFormedChecker.Check("body { color: red; }\n/* not closed\n");

        result.IsWellFormed.Should().BeFalse();
        result.Message.Should().Contain(LocalizedText.Fragment("Css_UnterminatedComment"));
    }
}
