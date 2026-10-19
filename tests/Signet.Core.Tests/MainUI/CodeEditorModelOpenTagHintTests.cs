using System;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>
/// Tests of the opening tag hint in <see cref="CodeEditorModel"/>: <see cref="CodeEditorModel.GetCloseTagAt"/> (the closing
/// tag under the mouse) and <see cref="CodeEditorModel.GetOpenTagOfCloseTag"/> (its opening tag).
/// </summary>
public sealed class CodeEditorModelOpenTagHintTests
{
    private const string OuterOpen = "<div class=\"chapter\" id=\"ch1\">";
    private const string InnerOpen = "<div\n    class=\"note\">";

    private const string Xhtml =
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n<body>\n" + OuterOpen + "\n" + InnerOpen + "<p>text</p></div>\n"
        + "<!-- </div> -->\n</div>\n</span>\n</body>\n</html>\n";

    private static CodeEditorModel NewModel(TempDir temp, string text)
    {
        var resource = new HtmlResource(temp.Path, temp.Combine("ch.xhtml"));
        resource.SetText(text);
        return new CodeEditorModel(resource);
    }

    private static int NthIndexOf(string text, string value, int n)
    {
        int index = -1;
        for (int i = 0; i <= n; i++)
        {
            index = text.IndexOf(value, index + 1, StringComparison.Ordinal);
        }

        return index;
    }

    [Fact]
    public void Every_character_of_a_closing_tag_hits_it_and_the_text_around_does_not()
    {
        // Arrange
        using TempDir temp = new();
        CodeEditorModel model = NewModel(temp, Xhtml);
        int close = Xhtml.IndexOf("</div>", StringComparison.Ordinal);

        // Act & Assert
        for (int i = close; i < close + "</div>".Length; i++)
        {
            model.GetCloseTagAt(i).Should().Be((close, "</div>".Length), $"offset {i} is in the closing tag");
        }

        // Act & Assert
        model.GetCloseTagAt(close - 1).Should().Be((close - "</p>".Length, "</p>".Length), "the character before is the '>' of </p>");

        // Act & Assert
        model.GetCloseTagAt(Xhtml.IndexOf("text", StringComparison.Ordinal)).Should().BeNull();

        // Act & Assert
        model.GetCloseTagAt(Xhtml.IndexOf(OuterOpen, StringComparison.Ordinal) + 3).Should().BeNull("an opening tag is not a closing one");
    }

    [Fact]
    public void Nested_closing_tags_get_their_own_opening_tags_even_when_multiline()
    {
        // Arrange
        using TempDir temp = new();
        CodeEditorModel model = NewModel(temp, Xhtml);
        int innerClose = NthIndexOf(Xhtml, "</div>", 0);
        int outerClose = NthIndexOf(Xhtml, "</div>", 2);

        // Act & Assert
        model.GetOpenTagOfCloseTag(innerClose).Should().Be((Xhtml.IndexOf(InnerOpen, StringComparison.Ordinal), InnerOpen.Length));

        // Act & Assert
        model.GetOpenTagOfCloseTag(outerClose).Should().Be((Xhtml.IndexOf(OuterOpen, StringComparison.Ordinal), OuterOpen.Length));
    }

    [Fact]
    public void A_closing_tag_inside_a_comment_or_without_an_opening_tag_has_no_hint()
    {
        // Arrange
        using TempDir temp = new();
        CodeEditorModel model = NewModel(temp, Xhtml);
        int inComment = NthIndexOf(Xhtml, "</div>", 1);
        int stray = Xhtml.IndexOf("</span>", StringComparison.Ordinal);

        // Act & Assert
        model.GetOpenTagOfCloseTag(inComment).Should().BeNull();

        // Act & Assert
        model.GetOpenTagOfCloseTag(stray).Should().BeNull();
    }

    [Fact]
    public void Css_has_no_closing_tags()
    {
        // Arrange
        using TempDir temp = new();
        var css = new CssResource(temp.Path, temp.Combine("s.css"));
        css.SetText("/* </div> */ p { color: red }");
        CodeEditorModel model = new(css);

        // Act & Assert
        model.GetCloseTagAt(4).Should().BeNull();

        // Act & Assert
        model.GetOpenTagOfCloseTag(3).Should().BeNull();
    }
}
