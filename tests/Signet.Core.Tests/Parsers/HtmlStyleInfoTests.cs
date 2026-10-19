using System.Linq;
using AwesomeAssertions;
using Signet.Core.Parsers;
using Xunit;

namespace Signet.Core.Tests.Parsers;

/// <summary>Tests for <see cref="HtmlStyleInfo"/>: <c>&lt;style&gt;</c> blocks in XHTML.</summary>
public sealed class HtmlStyleInfoTests
{
    private const string Html =
        "<html><head>\n" +
        "<style type=\"text/css\">\n.a { color: red }\n.unused { x: 1 }\np.b { y: 2 }\n</style>\n" +
        "</head><body>\n" +
        "<p class=\"a\">hi</p>\n" +
        "<style>.c { z: 3 }</style>\n" +
        "</body></html>\n";

    [Fact]
    public void Constructor_FindsEveryStyleBlock()
    {
        // Act
        HtmlStyleInfo info = new(Html);

        // Assert
        info.HasStyles.Should().BeTrue();
        info.Styles.Should().HaveCount(2);
    }

    [Fact]
    public void HasStyles_FalseWhenNoStyleElement()
    {
        // Act & Assert
        new HtmlStyleInfo("<html><body><p>text</p></body></html>").HasStyles.Should().BeFalse();
    }

    [Fact]
    public void Constructor_IgnoresStyleBlockAtPositionZero()
    {
        // A style block is only recognised when it starts after position 0 ("styleStart > 0").
        // Act
        HtmlStyleInfo info = new("<style>.x { a: 1 }</style><p>y</p>");

        // Assert
        info.HasStyles.Should().BeFalse();
    }

    [Fact]
    public void GetAllSelectors_AggregatesAcrossBlocksWithSourceOffsets()
    {
        // Arrange
        HtmlStyleInfo info = new(Html);

        // Act
        var selectors = info.GetAllSelectors();

        // Assert
        selectors.Select(s => s.Text).Should().Equal(".a", ".unused", "p.b", ".c");
        foreach (CssSelector selector in selectors)
        {
            Html.Substring(selector.Pos, selector.Text.Length).Should().Be(selector.Text);
        }
    }

    [Fact]
    public void GetCssSelectorForElementClass_SearchesEveryBlock()
    {
        // Arrange
        HtmlStyleInfo info = new(Html);

        // Act & Assert
        info.GetCssSelectorForElementClass("p", "b")!.Text.Should().Be("p.b");
    }

    [Fact]
    public void GetAllPropertyValues_AggregatesAcrossBlocks()
    {
        // Arrange
        HtmlStyleInfo info = new(Html);

        // Act & Assert
        info.GetAllPropertyValues("z").Should().Equal("3");
    }

    [Fact]
    public void GetReformattedCssText_ReplacesEachBlockContentWithFormattedText()
    {
        // Arrange
        HtmlStyleInfo info = new(Html);

        // Act
        string result = info.GetReformattedCssText(true);

        // Assert
        result.Should().Contain("<style type=\"text/css\">\n.a {\n  color: red;\n}");
        result.Should().Contain("<style>\n.c {\n  z: 3;\n}\n</style>");
        result.Should().StartWith("<html><head>").And.EndWith("</body></html>\n");
    }

    [Fact]
    public void RemoveMatchingSelectors_RemovesFromMatchingBlockAndLeavesOthersIntact()
    {
        // Arrange
        HtmlStyleInfo info = new(Html);

        // Act
        CssSelector unused = info.GetAllSelectors().Single(s => s.Text == ".unused");

        string result = info.RemoveMatchingSelectors(new[] { unused });

        // Assert
        result.Should().NotContain(".unused");
        // A block without a match (.c) is left verbatim.
        result.Should().Contain("<style>.c { z: 3 }</style>");
    }

    [Fact]
    public void GetCssProperties_SplitsWellFormedAndMarksMalformed()
    {
        // Arrange
        const string text = "color: red; font-weight: bold; just-a-name";

        // Act
        var props = HtmlStyleInfo.GetCssProperties(text, 0, text.Length);

        // Assert
        props.Should().Equal(
            new CssProperty("color", "red"),
            new CssProperty("font-weight", "bold"),
            new CssProperty("just-a-name", null));
    }

    [Fact]
    public void GetCssProperties_ReturnsEmptyForDegenerateRange()
    {
        // Act & Assert
        HtmlStyleInfo.GetCssProperties("x: y", 3, 3).Should().BeEmpty();
    }

    [Fact]
    public void FormatCssProperties_MultiLineAndSingleLineAndEmpty()
    {
        // Arrange
        var props = new[] { new CssProperty("color", "red"), new CssProperty("margin", "0") };

        // Act & Assert
        HtmlStyleInfo.FormatCssProperties(props, multipleLineFormat: false)
            .Should().Be(" color: red; margin: 0; ");

        // Act & Assert
        HtmlStyleInfo.FormatCssProperties(props, multipleLineFormat: true)
            .Should().Be("\n    color: red;\n    margin: 0;\n");

        // Act & Assert
        HtmlStyleInfo.FormatCssProperties(System.Array.Empty<CssProperty>(), multipleLineFormat: true)
            .Should().Be("\n");

        // Act & Assert
        HtmlStyleInfo.FormatCssProperties(System.Array.Empty<CssProperty>(), multipleLineFormat: false)
            .Should().BeEmpty();
    }
}
