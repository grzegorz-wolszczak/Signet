using System;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>
/// Parametrized tests of <see cref="BookPath"/> on path examples covering
/// the path helper behavior.
/// </summary>
public sealed class BookPathTests
{
    [Theory]
    [InlineData("OEBPS/Text/../Styles/main.css", "OEBPS/Styles/main.css")]
    [InlineData("OEBPS/./Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml")]
    [InlineData("OEBPS/Text/sub/../../images/c.png", "OEBPS/images/c.png")]
    [InlineData("EPUB/nav.xhtml", "EPUB/nav.xhtml")]
    [InlineData("../../foo/bar.xhtml", "foo/bar.xhtml")]
    [InlineData("a/b/c/../../d", "a/d")]
    public void ResolveRelativeSegments_collapses_dot_and_dotdot(string input, string expected)
    {
        BookPath.ResolveRelativeSegments(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("ch1.xhtml", "OEBPS/Text", "OEBPS/Text/ch1.xhtml")]
    [InlineData("../Styles/main.css", "OEBPS/Text", "OEBPS/Styles/main.css")]
    [InlineData("../../images/pic.png", "OEBPS/Text/sub", "OEBPS/images/pic.png")]
    [InlineData("cover.xhtml", "", "cover.xhtml")]
    [InlineData("OEBPS/content.opf", "", "OEBPS/content.opf")]
    [InlineData("ch1.xhtml", "OEBPS/Text/", "OEBPS/Text/ch1.xhtml")]
    public void BuildBookPath_resolves_relative_href_against_start_folder(
        string destRelativePath, string startFolder, string expected)
    {
        BookPath.BuildBookPath(destRelativePath, startFolder).Should().Be(expected);
    }

    [Theory]
    [InlineData("OEBPS/Text/ch1.xhtml", "OEBPS/Text")]
    [InlineData("OEBPS/content.opf", "OEBPS")]
    [InlineData("content.opf", "")]
    [InlineData("a/b/c/d.xhtml", "a/b/c")]
    public void StartingDir_returns_parent_folder(string fileBookPath, string expected)
    {
        BookPath.StartingDir(fileBookPath).Should().Be(expected);
    }

    [Theory]
    // the same directory
    [InlineData("OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch2.xhtml", "ch2.xhtml")]
    // a parallel directory - one step up
    [InlineData("OEBPS/Text/ch1.xhtml", "OEBPS/Styles/main.css", "../Styles/main.css")]
    // descending from the source file directory
    [InlineData("OEBPS/content.opf", "OEBPS/Text/ch1.xhtml", "Text/ch1.xhtml")]
    // only the root in common - two steps up
    [InlineData("OEBPS/Text/a.xhtml", "images/pic.png", "../../images/pic.png")]
    // identical paths
    [InlineData("OEBPS/Text/ch1.xhtml", "OEBPS/Text/ch1.xhtml", "")]
    // the source file in the root
    [InlineData("a.xhtml", "b.xhtml", "b.xhtml")]
    // deep nesting on both sides
    [InlineData("a/b/c/d/from.xhtml", "a/b/x/y/to.xhtml", "../../x/y/to.xhtml")]
    public void Relative_builds_href_from_source_file_to_target_file(
        string fromFileBookPath, string toFileBookPath, string expected)
    {
        BookPath.Relative(fromFileBookPath, toFileBookPath).Should().Be(expected);
    }

    [Theory]
    [InlineData("OEBPS/Text/ch2.xhtml", "OEBPS/Text", "ch2.xhtml")]
    [InlineData("OEBPS/Text", "OEBPS/Text", "")]
    [InlineData("OEBPS/Styles/main.css", "OEBPS/Text", "../Styles/main.css")]
    [InlineData("anything/here.xhtml", "", "anything/here.xhtml")]
    public void RelativePath_is_computed_from_a_starting_directory(
        string destination, string startDir, string expected)
    {
        BookPath.RelativePath(destination, startDir).Should().Be(expected);
    }

    [Fact]
    public void Relative_then_BuildBookPath_round_trips()
    {
        const string from = "OEBPS/Text/ch1.xhtml";
        const string to = "OEBPS/Styles/fonts/../main.css";
        string resolvedTo = BookPath.ResolveRelativeSegments(to);

        string href = BookPath.Relative(from, resolvedTo);
        string back = BookPath.BuildBookPath(href, BookPath.StartingDir(from));

        back.Should().Be(resolvedTo);
    }

    [Theory]
    [InlineData(new[] { "OEBPS/Text/a.xhtml", "OEBPS/Text/b.xhtml" }, "OEBPS/Text/")]
    [InlineData(new[] { "OEBPS/Text/a.xhtml", "OEBPS/Styles/m.css" }, "OEBPS/")]
    [InlineData(new[] { "a/x.xhtml", "b/y.xhtml" }, "/")]
    [InlineData(new[] { "OEBPS/only.xhtml" }, "OEBPS/only.xhtml/")]
    [InlineData(new[] { "OEBPS/Text/a.xhtml", "OEBPS/Text/sub/deep/b.xhtml", "OEBPS/Text/c.xhtml" }, "OEBPS/Text/")]
    public void LongestCommonPath_returns_shared_prefix_with_trailing_separator(
        string[] filePaths, string expected)
    {
        BookPath.LongestCommonPath(filePaths).Should().Be(expected);
    }

    [Fact]
    public void LongestCommonPath_of_empty_collection_is_empty()
    {
        BookPath.LongestCommonPath(Array.Empty<string>()).Should().BeEmpty();
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        FluentActions.Invoking(() => BookPath.ResolveRelativeSegments(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => BookPath.BuildBookPath(null!, "")).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => BookPath.Relative("a", null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => BookPath.LongestCommonPath(null!)).Should().Throw<ArgumentNullException>();
    }
}
