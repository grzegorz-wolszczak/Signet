using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of tag structure operations.
/// </summary>
public sealed class TagStructureOperationsTests
{
    private const string Doc = "<html><body><div id=\"d\"><p class=\"a\" id=\"p1\">one <b>two</b> three</p></div></body></html>";

    private static int At(string marker, int offset = 0) => Doc.IndexOf(marker, System.StringComparison.Ordinal) + offset;

    [Fact]
    public void Text_position_is_enclosed_by_the_innermost_open_element()
    {
        EnclosingElement? e = TagStructureOperations.FindEnclosingElement(Doc, At("three", 2));

        e.Should().NotBeNull();
        e!.Value.Name.Should().Be("p");
        e.Value.OpenPos.Should().Be(At("<p"));
        e.Value.ClosePos.Should().Be(At("</p>"));
    }

    [Fact]
    public void Position_after_a_closed_child_is_not_enclosed_by_the_child()
    {
        TagStructureOperations.FindEnclosingElement(Doc, At("</b>", 4))!.Value.Name.Should().Be("p");
        TagStructureOperations.FindEnclosingElement(Doc, At("two", 1))!.Value.Name.Should().Be("b");
    }

    [Theory]
    [InlineData("<p class", "p")]
    [InlineData("</p>", "p")]
    [InlineData("<b>", "b")]
    public void Position_inside_a_tag_definition_refers_to_that_element(string marker, string name)
    {
        TagStructureOperations.FindEnclosingElement(Doc, At(marker, 2))!.Value.Name.Should().Be(name);
    }

    [Fact]
    public void Jump_targets_and_contents_range_follow_the_tag_structure()
    {
        int caret = At("three", 1);

        TagStructureOperations.OpeningTagCaret(Doc, caret).Should().Be(At("<p") + 1);
        TagStructureOperations.ClosingTagCaret(Doc, caret).Should().Be(At("</p>") + 2);
        TagStructureOperations.TagContentsRange(Doc, caret).Should().Be((At("one"), At("</p>")));
    }

    [Fact]
    public void Rename_changes_opening_and_closing_tag_and_keeps_attributes()
    {
        FormatEdit edit = TagStructureOperations.RenameTag(Doc, At("three", 1), "blockquote");

        edit.Changed.Should().BeTrue();
        edit.Text.Should().Contain("<blockquote class=\"a\" id=\"p1\">one <b>two</b> three</blockquote>");
        edit.SelectionStart.Should().Be(At("three", 1) + "blockquote".Length - 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData("a b")]
    public void Rename_rejects_an_invalid_name(string name)
    {
        FormatEdit edit = TagStructureOperations.RenameTag(Doc, At("three"), name);

        edit.Changed.Should().BeFalse();
        edit.StatusMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Rename_refuses_an_unclosed_element()
    {
        FormatEdit edit = TagStructureOperations.RenameTag("<body><p>open", "<body><p>op".Length, "div");

        edit.Changed.Should().BeFalse();
        edit.StatusMessage.Should().Contain("<p>");
    }

    [Fact]
    public void Split_inserts_closing_tag_and_a_copy_of_the_opening_tag_without_id()
    {
        int caret = At(" three");

        FormatEdit edit = TagStructureOperations.SplitTag(Doc, caret);

        edit.Text.Should().Contain("<b>two</b></p><p class=\"a\"> three</p>");
        edit.SelectionStart.Should().Be(caret + "</p><p class=\"a\">".Length);
    }

    [Fact]
    public void Split_of_an_indented_block_starts_a_new_line_with_the_same_indent()
    {
        const string text = "<body>\n    <p>one two</p>\n</body>";

        FormatEdit edit = TagStructureOperations.SplitTag(text, text.IndexOf(" two", System.StringComparison.Ordinal));

        edit.Text.Should().Be("<body>\n    <p>one</p>\n    <p> two</p>\n</body>");
    }

    [Fact]
    public void Split_is_refused_inside_a_tag_definition()
    {
        TagStructureOperations.SplitTag(Doc, At("<p class", 3)).Changed.Should().BeFalse();
    }

    [Theory]
    [InlineData("<body><p>text</", "p")]
    [InlineData("<body><p>a <em>b</", "em")]
    [InlineData("<body><p>a <em>b</em></", "p")]
    [InlineData("<body><p>a <br/></", "p")]
    [InlineData("<body><p>a</p></", "body")]
    public void Auto_close_names_the_innermost_open_element(string typed, string expected)
    {
        TagStructureOperations.AutoCloseTagName(typed + "\n</body>", typed.Length).Should().Be(expected);
    }

    [Theory]
    [InlineData("<body><p>text<", 0)]
    [InlineData("<body><p>text</p></", -3)]
    public void Auto_close_does_nothing_without_a_fresh_slash_or_inside_a_tag(string text, int shift)
    {
        TagStructureOperations.AutoCloseTagName(text, text.Length + shift).Should().BeNull();
    }

    [Fact]
    public void Auto_close_does_nothing_when_the_slash_is_already_part_of_a_tag()
    {
        const string text = "<body><p>x</p>";
        int caret = "<body><p>x</".Length;

        TagStructureOperations.AutoCloseTagName(text, caret).Should().BeNull();
    }
}
