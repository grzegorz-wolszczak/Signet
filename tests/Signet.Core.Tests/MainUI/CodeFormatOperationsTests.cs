using System;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of <see cref="CodeFormatOperations"/> (the Format menu in Code Editor): one
/// scenario per operation, asserting the resulting text and well-formedness.
/// </summary>
public sealed class CodeFormatOperationsTests
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

    [Fact]
    public void ToggleInline_wraps_selection_when_not_already_bold()
    {
        // Arrange
        string text = Doc("<p>Hello world</p>");
        (int start, int end) = Range(text, "world");

        // Act
        FormatEdit result = CodeFormatOperations.ToggleInline(text, start, end, "b");

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Contain("<p>Hello <b>world</b></p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void ToggleInline_is_idempotent_and_removes_the_surrounding_tag()
    {
        // Arrange
        string text = Doc("<p>Hello <b>world</b></p>");
        (int start, int end) = Range(text, "world");

        // Act
        FormatEdit result = CodeFormatOperations.ToggleInline(text, start, end, "b");

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Contain("<p>Hello world</p>");
        result.Text.Should().NotContain("<b>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void ToggleInline_splits_a_partially_selected_element()
    {
        // Arrange
        string text = Doc("<p><b>aabbcc</b></p>");
        (int start, int end) = Range(text, "bb");

        // Act
        FormatEdit result = CodeFormatOperations.ToggleInline(text, start, end, "b");

        // Assert
        result.Text.Should().Contain("<p><b>aa</b>bb<b>cc</b></p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void FormatBlock_replaces_the_block_tag_with_a_heading()
    {
        // Arrange
        string text = Doc("<p>Chapter One</p>");
        (int start, int end) = Range(text, "Chapter");

        // Act
        FormatEdit result = CodeFormatOperations.FormatBlock(text, start, end, "h1", preserveAttributes: false);

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Contain("<h1>Chapter One</h1>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void FormatBlock_can_preserve_existing_attributes()
    {
        // Arrange
        string text = Doc("<p class=\"lead\">Chapter One</p>");
        (int start, int end) = Range(text, "Chapter");

        // Act
        FormatEdit result = CodeFormatOperations.FormatBlock(text, start, end, "h2", preserveAttributes: true);

        // Assert
        result.Text.Should().Contain("<h2 class=\"lead\">Chapter One</h2>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void ApplyList_wraps_paragraphs_into_an_unordered_list()
    {
        // Arrange
        string text = Doc("one\ntwo");
        int start = text.IndexOf("one", StringComparison.Ordinal);
        int end = text.IndexOf("two", StringComparison.Ordinal) + 3;

        // Act
        FormatEdit result = CodeFormatOperations.ApplyList(text, start, end, "ul");

        // Assert
        result.Text.Should().Contain("<ul>");
        result.Text.Should().Contain("<li>one</li>");
        result.Text.Should().Contain("<li>two</li>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void WrapInElement_indents_selection_in_a_blockquote()
    {
        // Arrange
        string text = Doc("<p>quote me</p>");
        (int start, int end) = Range(text, "<p>quote me</p>");

        // Act
        FormatEdit result = CodeFormatOperations.WrapInElement(text, start, end, "blockquote", unwrap: false);

        // Assert
        result.Text.Should().Contain("<blockquote>");
        result.Text.Should().Contain("<p>quote me</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void WrapInElement_outdents_by_removing_the_blockquote()
    {
        // Arrange
        string text = Doc("<blockquote>\n    <p>quote me</p>\n</blockquote>");
        int start = text.IndexOf("<blockquote>", StringComparison.Ordinal);
        int end = text.IndexOf("</blockquote>", StringComparison.Ordinal) + "</blockquote>".Length;

        // Act
        FormatEdit result = CodeFormatOperations.WrapInElement(text, start, end, "blockquote", unwrap: true);

        // Assert
        result.Text.Should().NotContain("<blockquote>");
        result.Text.Should().Contain("<p>quote me</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void FormatStyle_sets_a_text_align_style_attribute_on_the_block()
    {
        // Arrange
        string text = Doc("<p>centered</p>");
        (int start, int end) = Range(text, "centered");

        // Act
        FormatEdit result = CodeFormatOperations.FormatStyle(text, start, end, "text-align", "center");

        // Assert
        result.Text.Should().Contain("<p style=\"text-align: center;\">centered</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void FormatStyle_toggles_an_existing_matching_style_off()
    {
        // Arrange
        string text = Doc("<p style=\"text-align: center;\">centered</p>");
        (int start, int end) = Range(text, "centered");

        // Act
        FormatEdit result = CodeFormatOperations.FormatStyle(text, start, end, "text-align", "center");

        // Assert
        result.Text.Should().Contain("<p>centered</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void SetTextDirection_adds_a_dir_attribute()
    {
        // Arrange
        string text = Doc("<p>مرحبا</p>");
        (int start, int end) = Range(text, "مرحبا");

        // Act
        FormatEdit result = CodeFormatOperations.SetTextDirection(text, start, end, "rtl");

        // Assert
        result.Text.Should().Contain("<p dir=\"rtl\">مرحبا</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void SetTextDirection_with_empty_value_removes_the_dir_attribute()
    {
        // Arrange
        string text = Doc("<p dir=\"rtl\">مرحبا</p>");
        (int start, int end) = Range(text, "مرحبا");

        // Act
        FormatEdit result = CodeFormatOperations.SetTextDirection(text, start, end, string.Empty);

        // Assert
        result.Text.Should().Contain("<p>مرحبا</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void ChangeCase_uppercases_the_selected_text()
    {
        // Arrange
        string text = Doc("<p>quiet</p>");
        (int start, int end) = Range(text, "quiet");

        // Act
        FormatEdit result = CodeFormatOperations.ChangeCase(text, start, end, Casing.Uppercase);

        // Assert
        result.Text.Should().Contain("<p>QUIET</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void ChangeCase_refuses_a_selection_that_contains_markup()
    {
        // Arrange
        string text = Doc("<p>a <b>b</b></p>");
        (int start, _) = Range(text, "a <b>b");
        int end = start + "a <b>b".Length;

        // Act
        FormatEdit result = CodeFormatOperations.ChangeCase(text, start, end, Casing.Uppercase);

        // Assert
        result.Changed.Should().BeFalse();
    }

    [Fact]
    public void RemoveFormatting_strips_inline_tags_from_the_selection()
    {
        // Arrange
        string text = Doc("<p>x <b>bold</b> y</p>");
        (int start, int end) = Range(text, "<b>bold</b>");

        // Act
        FormatEdit result = CodeFormatOperations.RemoveFormatting(text, start, end);

        // Assert
        result.Text.Should().Contain("<p>x bold y</p>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void RemoveTagPair_removes_the_surrounding_element_but_keeps_content()
    {
        // Arrange
        string text = Doc("<p><em>keep me</em></p>");
        int caret = text.IndexOf("<em>", StringComparison.Ordinal) + 2;

        // Act & Assert
        CodeFormatOperations.RemoveTagPairAllowed(text, caret).Should().BeTrue();

        // Act
        FormatEdit result = CodeFormatOperations.RemoveTagPair(text, caret);

        // Assert
        result.Text.Should().Contain("<p>keep me</p>");
        result.Text.Should().NotContain("<em>");
        AssertWellFormed(result.Text);
    }

    [Fact]
    public void InsertClosingTag_closes_the_nearest_open_tag()
    {
        // Arrange
        string text = Doc("<div><p>text");
        int caret = text.IndexOf("text", StringComparison.Ordinal) + "text".Length;

        // Act
        FormatEdit result = CodeFormatOperations.InsertClosingTag(text, caret);

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Contain("<div><p>text</p>");
    }

    [Fact]
    public void InsertClosingTag_reports_when_there_is_nothing_to_close()
    {
        // Arrange
        string text = Doc("<p>done</p>");
        int caret = text.IndexOf("done</p>", StringComparison.Ordinal) + "done</p>".Length;

        // Act
        FormatEdit result = CodeFormatOperations.InsertClosingTag(text, caret);

        // Assert
        result.Changed.Should().BeFalse();
        result.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CaretBlockElementName_returns_the_innermost_block_tag()
    {
        // Arrange
        string text = Doc("<h3>Title</h3>");
        int caret = text.IndexOf("Title", StringComparison.Ordinal) + 2;

        // Act & Assert
        CodeFormatOperations.CaretBlockElementName(text, caret).Should().Be("h3");
    }

    [Fact]
    public void RemoveFormattingAllowed_is_false_without_a_selection()
    {
        // Arrange
        string text = Doc("<p>abc</p>");
        int caret = text.IndexOf("abc", StringComparison.Ordinal);

        // Act & Assert
        CodeFormatOperations.RemoveFormattingAllowed(text, caret, caret).Should().BeFalse();
    }

    [Theory]
    [InlineData(CodeEditorSyntax.Html)]
    [InlineData(CodeEditorSyntax.Xml)]
    [InlineData(CodeEditorSyntax.Css)]
    [InlineData(CodeEditorSyntax.JavaScript)]
    public void IsCommentSyntaxSupported_is_true_for_markup_and_style_syntaxes(CodeEditorSyntax syntax)
    {
        // Act & Assert
        CodeFormatOperations.IsCommentSyntaxSupported(syntax).Should().BeTrue();
    }

    [Theory]
    [InlineData(CodeEditorSyntax.PlainText)]
    [InlineData(CodeEditorSyntax.Json)]
    public void IsCommentSyntaxSupported_is_false_for_syntaxes_without_comments(CodeEditorSyntax syntax)
    {
        // Act & Assert
        CodeFormatOperations.IsCommentSyntaxSupported(syntax).Should().BeFalse();
    }

    [Fact]
    public void ToggleComment_returns_unchanged_for_unsupported_syntax()
    {
        // Arrange
        const string text = "hello world";

        // Act
        FormatEdit result = CodeFormatOperations.ToggleComment(text, 0, 5, CodeEditorSyntax.PlainText);

        // Assert
        result.Changed.Should().BeFalse();
    }

    [Fact]
    public void ToggleComment_wraps_a_css_selection_with_block_markers()
    {
        // Arrange
        const string text = "p { color: red; }";

        // Act
        FormatEdit result = CodeFormatOperations.ToggleComment(text, 0, text.Length, CodeEditorSyntax.Css);

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Be("/*" + text + "*/");
    }

    [Fact]
    public void ToggleComment_removes_markers_when_selection_is_already_a_full_comment()
    {
        // Arrange
        const string commented = "/*p { color: red; }*/";

        // Act
        FormatEdit result = CodeFormatOperations.ToggleComment(commented, 0, commented.Length, CodeEditorSyntax.Css);

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Be("p { color: red; }");
    }

    [Fact]
    public void ToggleComment_uncomments_when_caret_sits_inside_a_multiline_block_comment()
    {
        // Arrange
        string text = "/*\ncomment text line\n*/";
        int lineStart = text.IndexOf("comment", StringComparison.Ordinal);
        int caret = lineStart + 3;

        // Act
        FormatEdit result = CodeFormatOperations.ToggleComment(text, caret, caret, CodeEditorSyntax.Css);

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Be("\ncomment text line\n");
    }

    [Fact]
    public void ToggleComment_wraps_the_current_line_when_there_is_no_selection()
    {
        // Arrange
        string text = Doc("<p>keep me</p>");
        int caret = text.IndexOf("<p>keep me</p>", StringComparison.Ordinal) + 3;

        // Act
        FormatEdit result = CodeFormatOperations.ToggleComment(text, caret, caret, CodeEditorSyntax.Html);

        // Assert
        result.Changed.Should().BeTrue();
        result.Text.Should().Contain("<!--<p>keep me</p>-->");
    }

    [Fact]
    public void ToggleComment_uses_xml_style_markers_for_xhtml()
    {
        // Arrange
        const string text = "<span>hi</span>";

        // Act
        FormatEdit result = CodeFormatOperations.ToggleComment(text, 0, text.Length, CodeEditorSyntax.Html);

        // Assert
        result.Text.Should().Be("<!--" + text + "-->");
    }

    [Fact]
    public void ToggleComment_returns_unchanged_when_selection_is_only_whitespace()
    {
        // Arrange
        const string text = "   ";

        // Act
        FormatEdit result = CodeFormatOperations.ToggleComment(text, 0, text.Length, CodeEditorSyntax.Css);

        // Assert
        result.Changed.Should().BeFalse();
    }
}
