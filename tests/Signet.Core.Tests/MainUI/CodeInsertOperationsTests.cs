using System;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of <see cref="CodeInsertOperations"/> (the Insert menu in Code Editor): one
/// scenario per operation, asserting the resulting text and well-formedness.
/// </summary>
public sealed class CodeInsertOperationsTests
{
    private const string Head =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<head><title>t</title></head>\n<body>\n";

    private const string Tail = "\n</body>\n</html>\n";

    private static string Doc(string bodyInner) => Head + bodyInner + Tail;

    private static void AssertWellFormed(string text) =>
        WellFormedChecker.CheckXhtmlStructure(text, "3.0").IsWellFormed.Should().BeTrue();

    private static (int Start, int End) Range(string text, string needle)
    {
        int i = text.IndexOf(needle, StringComparison.Ordinal);
        i.Should().BeGreaterThanOrEqualTo(0);
        return (i, i + needle.Length);
    }

    private static int CaretAfter(string text, string needle) =>
        text.IndexOf(needle, StringComparison.Ordinal) + needle.Length;

    [Fact]
    public void InsertText_replaces_the_selection_with_the_given_string()
    {
        // Arrange
        string text = Doc("<p>alpha omega</p>");
        (int start, int end) = Range(text, "omega");

        // Act
        FormatEdit result = CodeInsertOperations.InsertText(text, start, end, "—");

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Contain("<p>alpha —</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void InsertText_at_a_caret_inserts_without_removing_anything()
    {
        // Arrange
        string text = Doc("<p>ab</p>");
        int caret = CaretAfter(text, "<p>a");

        // Act
        FormatEdit result = CodeInsertOperations.InsertText(text, caret, caret, "X");

        // Assert
        result.Text.Should().Contain("<p>aXb</p>");
        result.SelectionStart.Should().Be(caret + 1);
    }

    [Theory]
    [InlineData(ResourceType.Image, "<img alt=\"pic\" src=\"img/pic.png\"/>")]
    [InlineData(ResourceType.Svg, "<img alt=\"pic\" src=\"img/pic.png\"/>")]
    [InlineData(ResourceType.Audio, "<audio controls=\"controls\" src=\"img/pic.png\">pic</audio>")]
    [InlineData(ResourceType.Video, "<video controls=\"controls\" src=\"img/pic.png\">pic</video>")]
    [InlineData(ResourceType.Generic, "<a href=\"img/pic.png\">pic</a>")]
    public void BuildFileFragment_matches_the_resource_type(ResourceType type, string expected)
    {
        // Act & Assert
        CodeInsertOperations.BuildFileFragment(type, "img/pic.png", "pic").Should().Be(expected);
    }

    [Fact]
    public void InsertFileFragment_inserts_an_image_in_the_body()
    {
        // Arrange
        string text = Doc("<p>x</p>");
        int caret = CaretAfter(text, "<p>x</p>");

        // Act
        FormatEdit result = CodeInsertOperations.InsertFileFragment(
            text, caret, caret, "<img alt=\"a\" src=\"a.png\"/>");

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Contain("<p>x</p><img alt=\"a\" src=\"a.png\"/>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void InsertFileFragment_refuses_a_position_inside_a_tag()
    {
        // Arrange
        string text = Doc("<p class=\"a\">x</p>");
        int caret = CaretAfter(text, "<p clas");

        // Act
        FormatEdit result = CodeInsertOperations.InsertFileFragment(text, caret, caret, "<img src=\"a.png\"/>");

        // Assert
        result.Changed.Should().BeFalse();
        result.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void InsertId_adds_an_id_to_the_surrounding_anchor()
    {
        // Arrange
        string text = Doc("<p><a href=\"x.xhtml\">link</a></p>");
        int caret = CaretAfter(text, "<a href=\"x.xhtml\">li");

        // Act
        FormatEdit result = CodeInsertOperations.InsertId(text, caret, caret, "spot");

        // Assert
        result.Text.Should().Contain("<a href=\"x.xhtml\" id=\"spot\">link</a>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void InsertId_wraps_a_selection_in_a_new_anchor()
    {
        // Arrange
        string text = Doc("<p>a word b</p>");
        (int start, int end) = Range(text, "word");

        // Act
        FormatEdit result = CodeInsertOperations.InsertId(text, start, end, "w");

        // Assert
        result.Text.Should().Contain("<p>a <a id=\"w\">word</a> b</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void InsertId_at_a_caret_inserts_an_empty_anchor()
    {
        // Arrange
        string text = Doc("<p>ab</p>");
        int caret = CaretAfter(text, "<p>a");

        // Act
        FormatEdit result = CodeInsertOperations.InsertId(text, caret, caret, "m");

        // Assert
        result.Text.Should().Contain("<p>a<a id=\"m\"></a>b</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void InsertHyperlink_sets_href_on_an_existing_anchor()
    {
        // Arrange
        string text = Doc("<p><a id=\"k\">link</a></p>");
        int caret = CaretAfter(text, "<a id=\"k\">li");

        // Act
        FormatEdit result = CodeInsertOperations.InsertHyperlink(text, caret, caret, "target.xhtml#c1");

        // Assert
        result.Text.Should().Contain("<a id=\"k\" href=\"target.xhtml#c1\">link</a>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void InsertHyperlink_escapes_markup_characters_in_the_target()
    {
        // Arrange
        string text = Doc("<p>a word b</p>");
        (int start, int end) = Range(text, "word");

        // Act
        FormatEdit result = CodeInsertOperations.InsertHyperlink(text, start, end, "a.xhtml?x=1&y=2");

        // Assert
        result.Text.Should().Contain("<a href=\"a.xhtml?x=1&amp;y=2\">word</a>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void CurrentHrefValue_reads_the_href_under_the_caret()
    {
        // Arrange
        string text = Doc("<p><a href=\"chap.xhtml\">go</a></p>");
        int caret = CaretAfter(text, "<a href=\"chap.xhtml\">g");

        // Act & Assert
        CodeInsertOperations.CurrentHrefValue(text, caret).Should().Be("chap.xhtml");
    }

    [Fact]
    public void CurrentIdValue_reads_the_id_under_the_caret()
    {
        // Arrange
        string text = Doc("<p><a id=\"here\">go</a></p>");
        int caret = CaretAfter(text, "<a id=\"here\">g");

        // Act & Assert
        CodeInsertOperations.CurrentIdValue(text, caret).Should().Be("here");
    }

    [Fact]
    public void InsertIdAllowed_is_true_in_body_text_and_false_in_the_head()
    {
        // Arrange
        string text = Doc("<p>x</p>");

        // Act & Assert
        CodeInsertOperations.InsertIdAllowed(text, CaretAfter(text, "<p>")).Should().BeTrue();

        // Arrange
        int inHead = CaretAfter(text, "<title>");

        // Act & Assert
        CodeInsertOperations.InsertIdAllowed(text, inHead).Should().BeFalse();
    }

    [Fact]
    public void InsertHyperlinkAllowed_is_false_inside_a_non_anchor_opening_tag()
    {
        // Arrange
        string text = Doc("<p class=\"a\">x</p>");
        int inTag = CaretAfter(text, "<p cla");

        // Act & Assert
        CodeInsertOperations.InsertHyperlinkAllowed(text, inTag).Should().BeFalse();
    }

    [Fact]
    public void InsertFileAllowed_is_false_outside_the_body()
    {
        // Arrange
        string text = Doc("<p>x</p>");

        // Act & Assert
        CodeInsertOperations.InsertFileAllowed(text, 5).Should().BeFalse();
    }

    [Theory]
    [InlineData("chap1", true)]
    [InlineData("c-h.a:p", true)]
    [InlineData("1chap", false)]
    [InlineData("has space", false)]
    [InlineData("", false)]
    public void IsValidId_matches_the_xml_id_rules(string id, bool expected)
    {
        // Act & Assert
        CodeInsertOperations.IsValidId(id).Should().Be(expected);
    }

    [Fact]
    public void PasteClipText_without_a_selection_strips_the_placeholder_and_inserts_at_the_caret()
    {
        // Arrange
        string text = Doc("<p>ab</p>");
        int caret = CaretAfter(text, "<p>a");

        // Act
        FormatEdit result = CodeInsertOperations.PasteClipText(text, caret, caret, "<em>\\1</em>");

        // Assert
        result.Text.Should().Contain("<p>a<em></em>b</p>");
    }

    [Fact]
    public void PasteClipText_with_a_selection_wraps_it_by_replacing_the_placeholder()
    {
        // Arrange
        string text = Doc("<p>alpha omega</p>");
        (int start, int end) = Range(text, "omega");

        // Act
        FormatEdit result = CodeInsertOperations.PasteClipText(text, start, end, "<em>\\1</em>");

        // Assert
        result.Text.Should().Contain("<p>alpha <em>omega</em></p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void PasteClipText_with_an_empty_clip_reports_no_change()
    {
        // Arrange
        string text = Doc("<p>ab</p>");
        int caret = CaretAfter(text, "<p>a");

        // Act
        FormatEdit result = CodeInsertOperations.PasteClipText(text, caret, caret, string.Empty);

        // Assert
        result.Changed.Should().BeFalse();
        result.Text.Should().Be(text);
    }
}
