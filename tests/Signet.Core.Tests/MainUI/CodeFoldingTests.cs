using System.Linq;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of <see cref="CodeFolding"/> — the foldable regions of Code Editor: multi-line elements and comments in markup,
/// multi-line blocks in CSS; nothing that opens and closes on one line.
/// </summary>
public sealed class CodeFoldingTests
{
    // The text each region hides.
    private static string[] Hidden(string text, System.Collections.Generic.IReadOnlyList<FoldRegion> regions) =>
        regions.Select(r => text[r.StartOffset..r.EndOffset]).ToArray();

    [Fact]
    public void A_multi_line_element_folds_after_its_opening_tag_up_to_the_end_of_its_closing_tag()
    {
        // Arrange
        const string text = "<body>\n<div class=\"a\">\n  <p>x</p>\n</div>\n</body>";

        // Act
        var regions = CodeFolding.ForMarkup(text);

        // Assert
        Hidden(text, regions).Should().Equal(
            "\n<div class=\"a\">\n  <p>x</p>\n</div>\n</body>",
            "\n  <p>x</p>\n</div>");
        regions.Should().OnlyContain(r => r.Title == CodeFolding.Ellipsis);
    }

    [Theory]
    [InlineData("<p>one line</p>")]
    [InlineData("<div><span>a</span></div>")]
    [InlineData("<br/>\n<hr/>")]
    [InlineData("<!-- one line -->")]
    public void Nothing_that_opens_and_closes_on_one_line_is_foldable(string text)
    {
        // Act & Assert
        CodeFolding.ForMarkup(text).Should().BeEmpty();
    }

    [Fact]
    public void A_multi_line_comment_folds_after_its_opening()
    {
        // Arrange
        const string text = "<p>a</p>\n<!-- first\n second -->";

        // Act & Assert
        CodeFolding.ForMarkup(text).Should().ContainSingle()
            .Which.Should().Be(new FoldRegion(text.IndexOf(" first", System.StringComparison.Ordinal), text.Length, CodeFolding.Ellipsis));
    }

    [Fact]
    public void The_regions_are_ordered_by_their_start()
    {
        // Arrange
        const string text = "<a>\n<b>\n</b>\n<c>\n</c>\n</a>";

        // Act & Assert
        CodeFolding.ForMarkup(text).Select(r => r.StartOffset).Should().BeInAscendingOrder();

        // Act & Assert
        CodeFolding.ForMarkup(text).Should().HaveCount(3);
    }

    [Fact]
    public void Multi_line_css_blocks_fold_with_their_braces_and_nest()
    {
        // Arrange
        const string text = "p { color: red }\n@media print {\n  h1 {\n    color: black;\n  }\n}";

        // Act
        var regions = CodeFolding.ForCss(text);

        // Assert
        Hidden(text, regions).Should().Equal(
            "{\n  h1 {\n    color: black;\n  }\n}",
            "{\n    color: black;\n  }");
        regions.Should().OnlyContain(r => r.Title == CodeFolding.CssBlock);
    }

    [Fact]
    public void Braces_in_css_comments_and_strings_do_not_count()
    {
        // Arrange
        const string text = "/* { */\np::before {\n  content: \"}\";\n}\n/* } */";

        // Act & Assert
        CodeFolding.ForCss(text).Should().ContainSingle()
            .Which.StartOffset.Should().Be(text.IndexOf("{\n  content", System.StringComparison.Ordinal));
    }
}
