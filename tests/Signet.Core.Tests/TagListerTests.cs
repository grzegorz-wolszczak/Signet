using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Xunit;
using Signet.Core.Localization;

namespace Signet.Core.Tests;

/// <summary>Tests for <see cref="TagLister"/> — a tag scanner over source text.</summary>
public sealed class TagListerTests
{
    private const string Doc =
        "<html>\n" +
        "<head><title>T</title></head>\n" +
        "<body>\n" +
        "  <p id=\"a\" class=\"lead\">Hello <b>bold</b> world</p>\n" +
        "  <img src=\"x.png\" alt=\"x\"/>\n" +
        "</body>\n" +
        "</html>\n";

    public static TheoryData<string> AllCorpusXhtml()
    {
        TheoryData<string> data = new();
        foreach (string path in CorpusPaths.EnumerateFiles("*.xhtml")
                     .Where(p => !CorpusPaths.IsFaultyOnPurpose(p)))
        {
            data.Add(path);
        }

        return data;
    }

    /// <summary>
    /// <see cref="TagLister.For"/> keeps the lister it built last for the same string instance; a different string, even
    /// one built from the same pieces after an edit, always gets the tags of its own text.
    /// </summary>
    [Fact]
    public void For_lists_the_tags_of_the_text_given_also_after_the_text_changed()
    {
        // Arrange
        string before = Doc;
        string after = Doc.Replace("<b>bold</b>", "<i>it</i> <b>bold</b>", StringComparison.Ordinal);

        // Act
        TagLister first = TagLister.For(before);
        TagLister again = TagLister.For(before);
        TagLister edited = TagLister.For(after);

        // Assert
        first.Tags.Select(t => (t.Pos, t.TagName)).Should().Equal(new TagLister(before).Tags.Select(t => (t.Pos, t.TagName)));
        again.Tags.Select(t => (t.Pos, t.TagName)).Should().Equal(first.Tags.Select(t => (t.Pos, t.TagName)));
        edited.Source.Should().BeSameAs(after);
        edited.Tags.Select(t => t.TagName).Should().Contain("i", "the edited text has a tag the first one did not have");
    }

    [Fact]
    public void Tags_AreListedInSourceOrder_WithPositionsAndKinds()
    {
        // Arrange
        TagLister lister = new(Doc);

        // Act & Assert
        lister.Tags[0].Should().Match<TagLister.TagInfo>(t =>
            t.TagName == "html" && t.Kind == TagKind.Begin && t.Pos == 0 && t.Len == 6);

        // Act
        TagLister.TagInfo img = lister.Tags.Single(t => t.TagName == "img");

        // Assert
        img.Kind.Should().Be(TagKind.SelfClosing);
        Doc.Substring(img.Pos, img.Len).Should().Be("<img src=\"x.png\" alt=\"x\"/>");

        // The last real entry is followed by an end-of-list sentinel.
        lister.Tags[^1].Len.Should().Be(-1);
        lister.Tags[^1].Pos.Should().Be(-1);
    }

    [Fact]
    public void EndTags_LinkBackToTheirOpeningTag()
    {
        // Arrange
        TagLister lister = new(Doc);

        // Act
        int openP = Enumerable.Range(0, lister.Count).First(i => lister.At(i) is { TagName: "p", Kind: TagKind.Begin });
        int closeP = lister.FindCloseTagForOpen(openP);

        // Assert
        closeP.Should().BeGreaterThan(openP);
        lister.At(closeP).Kind.Should().Be(TagKind.End);
        lister.At(closeP).TagName.Should().Be("p");
        lister.At(closeP).OpenPos.Should().Be(lister.At(openP).Pos);
        lister.FindOpenTagForClose(closeP).Should().Be(openP);
    }

    [Fact]
    public void BodyTagsAndPositions_AreTracked()
    {
        // Arrange
        TagLister lister = new(Doc);

        // Act & Assert
        lister.FindBodyOpenTag().Should().BeGreaterThanOrEqualTo(0);

        // Act & Assert
        lister.FindBodyCloseTag().Should().BeGreaterThan(lister.FindBodyOpenTag());

        // Arrange
        int insideBody = Doc.IndexOf("Hello", StringComparison.Ordinal);
        int insideHead = Doc.IndexOf("<title>", StringComparison.Ordinal);

        // Act & Assert
        lister.IsPositionInBody(insideBody).Should().BeTrue();

        // Act & Assert
        lister.IsPositionInBody(insideHead).Should().BeFalse();
    }

    [Fact]
    public void PositionQueries_DistinguishOpenCloseAndText()
    {
        // Arrange
        TagLister lister = new(Doc);

        int inOpenTag = Doc.IndexOf("id=\"a\"", StringComparison.Ordinal);
        int inCloseTag = Doc.IndexOf("</b>", StringComparison.Ordinal) + 2;
        int inText = Doc.IndexOf("bold", StringComparison.Ordinal);

        // Act & Assert
        lister.IsPositionInTag(inOpenTag).Should().BeTrue();

        // Act & Assert
        lister.IsPositionInOpenTag(inOpenTag).Should().BeTrue();

        // Act & Assert
        lister.IsPositionInCloseTag(inOpenTag).Should().BeFalse();

        // Act & Assert
        lister.IsPositionInCloseTag(inCloseTag).Should().BeTrue();

        // Act & Assert
        lister.IsPositionInOpenTag(inCloseTag).Should().BeFalse();

        // Act & Assert
        lister.IsPositionInTag(inText).Should().BeFalse();
    }

    [Fact]
    public void FindFirstAndLastTag_RelativeToPosition()
    {
        // Arrange
        TagLister lister = new(Doc);
        int inText = Doc.IndexOf("bold", StringComparison.Ordinal);

        // Act & Assert
        lister.At(lister.FindLastTagOnOrBefore(inText)).Should().Match<TagLister.TagInfo>(
            t => t.TagName == "b" && t.Kind == TagKind.Begin);

        // Act & Assert
        lister.At(lister.FindFirstTagOnOrAfter(inText)).Should().Match<TagLister.TagInfo>(
            t => t.TagName == "b" && t.Kind == TagKind.End);
    }

    /// <summary>
    /// Both lookups halve the tag list instead of walking it, so they are checked against what they promise at every
    /// position of a real file: the last tag that starts at the position or before it, and the first one that ends
    /// after it. The index out of the range of the real tags is the sentinel at the end of the list.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void FindFirstAndLastTag_AgreeWithTheirDefinitionAtEveryPosition(string path)
    {
        // Arrange
        string text = File.ReadAllText(path);
        TagLister lister = new(text);
        int tags = lister.Count - 1;

        // Act & Assert
        for (int position = -1; position <= text.Length; position++)
        {
            int last = -1;
            int first = tags;
            for (int i = 0; i < tags; i++)
            {
                TagLister.TagInfo tag = lister.Tags[i];
                if (tag.Pos <= position)
                {
                    last = i;
                }

                if (tag.Pos + tag.Len > position && first == tags)
                {
                    first = i;
                }
            }

            lister.FindLastTagOnOrBefore(position).Should().Be(last, "the last tag on or before {0}", position);
            lister.FindFirstTagOnOrAfter(position).Should().Be(first, "the first tag on or after {0}", position);
        }
    }

    [Fact]
    public void GeneratePathToTag_ReturnsTagPathWithChildNumbers()
    {
        // Arrange
        TagLister lister = new(Doc);
        int insideBold = Doc.IndexOf("bold", StringComparison.Ordinal);

        // Act & Assert
        lister.GeneratePathToTag(insideBold).Should().StartWith("html").And.Contain("body").And.Contain("b ");
    }

    [Fact]
    public void MismatchedCloseTag_HasNoOpenPositionAndIsReportedAsNestingError()
    {
        // Arrange
        TagLister lister = new("<root><a><b>x</a></b></root>");

        // Act
        TagLister.TagInfo strayClose = lister.Tags.First(t => t.TagName == "a" && t.Kind == TagKind.End);

        // Assert
        strayClose.OpenPos.Should().Be(-1);

        // Act
        NestingError? error = lister.FindFirstNestingError();

        // Assert
        error.Should().NotBeNull();
        error!.Value.TagName.Should().Be("a");
        error.Value.Pos.Should().Be(lister.Source.IndexOf("</a>", StringComparison.Ordinal));
    }

    [Fact]
    public void TruncatedDocument_ReportsOutermostUnclosedTag()
    {
        // Arrange
        TagLister lister = new("<root><child>text");

        // Act
        NestingError? error = lister.FindFirstNestingError();

        // Assert
        error.Should().NotBeNull();
        error!.Value.TagName.Should().Be("root");
        error.Value.Message.Should().Be(CoreStrings.Format("Nesting_UnclosedTag", "root"));
    }

    [Fact]
    public void WellNestedDocument_HasNoNestingError()
    {
        // Act & Assert
        new TagLister(Doc).FindFirstNestingError().Should().BeNull();
    }

    [Fact]
    public void ParseAttribute_LocatesNameAndValueWithinTag()
    {
        // Act
        TagLister.AttInfo info = TagLister.ParseAttribute("<p id=\"a\" class=\"lead\">", "class");

        // Assert
        info.AName.Should().Be("class");
        info.AValue.Should().Be("lead");
        "<p id=\"a\" class=\"lead\">".Substring(info.VPos, info.VLen).Should().Be("lead");
    }

    // The expected positions were taken from the implementation before it worked on spans, so that the two are known to agree.
    [Theory]
    [InlineData("<p id=a class=lead>", "class", 8, 10, 14, 4, "class", "lead")] // not quoted
    [InlineData("<p id = \"a\"  class =  'x y' >", "class", 13, 14, 23, 3, "class", "x y")] // blanks around the equals sign, single quotes
    [InlineData("<p id=\"a\" id=\"b\">", "id", 10, 6, 14, 1, "id", "b")] // twice: the last one counts
    [InlineData("<p id=>", "id", 3, 3, 6, 0, "id", "")] // no value
    [InlineData("<p id>", "id", -1, -1, -1, -1, "", "")] // no equals sign: not an attribute here
    [InlineData("<p id=\"unclosed", "id", 3, 13, 7, 8, "id", "unclosed")] // the quote is never closed
    [InlineData("<p title=\"a>b\" id=\"z\">", "id", 15, 6, 19, 1, "id", "z")] // a '>' inside a value
    [InlineData("<a  href = x  id = y >", "id", 14, 6, 19, 1, "id", "y")]
    [InlineData("<p ID=\"a\" id=\"b\">", "id", 10, 6, 14, 1, "id", "b")] // names are case sensitive
    [InlineData("<p id=\"a\"class=\"b\">", "class", 9, 9, 16, 1, "class", "b")] // no blank between two attributes
    [InlineData("<p class= id=\"a\">", "class", 3, 13, 10, 6, "class", "id=\"a\"")] // an unquoted value runs to the next blank
    [InlineData("<p id=\"a\" id>", "id", 3, 6, 7, 1, "id", "a")]
    [InlineData("<p id='' class=''>", "class", 9, 8, 16, 0, "class", "")]
    public void ParseAttribute_MatchesWhatItGaveBeforeItWorkedOnSpans(string tag, string name, int pos, int len, int valuePos, int valueLen, string aName, string value)
    {
        // Act
        TagLister.AttInfo info = TagLister.ParseAttribute(tag, name);

        // Assert
        info.Should().Be(new TagLister.AttInfo(pos, len, valuePos, valueLen, aName, value));
    }

    // The paths of the tags are made when they are asked for. The expected texts were taken from the implementation that made them for every tag.
    [Fact]
    public void TagPath_OfEachTag_IsThePathWithTheChildNumbers()
    {
        // Arrange
        TagLister lister = TagLister.For("<?xml version=\"1.0\"?><!DOCTYPE html><html><head><title>t</title></head><body><p>a<b>c</b></p><br/><img src=\"x\"/></body></html>");

        // Act
        string[] paths = lister.Tags.Where(t => t.Len != -1).Select(t => $"{t.TagName}:{t.TagPath}").ToArray();

        // Assert
        paths.Should().Equal(
            "?xml:", "!DOCTYPE:", "html:html -1", "head:html 0,head -1", "title:html 0,head 0,title -1", "title:html 0,head -1", "head:html -1",
            "body:html 1,body -1", "p:html 1,body 0,p -1", "b:html 1,body 0,p 0,b -1", "b:html 1,body 0,p -1", "p:html 1,body -1",
            "br:html 1,body 1,br -1", "img:html 1,body 2,img -1", "body:html -1", "html:");
    }

    [Fact]
    public void TagPath_AfterCommentsCDataAndScripts_CountsOnlyTags()
    {
        // Arrange
        TagLister lister = TagLister.For("<html><body><!-- <p> --><![CDATA[ <b> ]]><p>x</p><script>if (a<b) {}</script></body></html>");

        // Act
        string[] paths = lister.Tags.Where(t => t.Len != -1).Select(t => $"{t.TagName}:{t.TagPath}").ToArray();

        // Assert
        paths.Should().Equal(
            "html:html -1", "body:html 0,body -1", "!--:", "![CDATA[:", "p:html 0,body 0,p -1", "p:html 0,body -1", "script:html 0,body 1,script -1",
            "script:html 0,body -1", "body:html -1", "html:");
    }

    [Fact]
    public void ParseAttribute_MissingAttribute_ReturnsNone()
    {
        // Act & Assert
        TagLister.ParseAttribute("<p id=\"a\">", "class").Should().Be(TagLister.AttInfo.None);
    }

    [Fact]
    public void SerializeAttribute_SwitchesToApostropheWhenValueHasDoubleQuote()
    {
        // Act & Assert
        TagLister.SerializeAttribute("title", "plain").Should().Be("title=\"plain\"");

        // Act & Assert
        TagLister.SerializeAttribute("title", "has \" quote").Should().Be("title='has \" quote'");
    }

    [Fact]
    public void ExtractAllAttributes_ReturnsAttributePortionWithoutTagNameOrSlash()
    {
        // Act & Assert
        TagLister.ExtractAllAttributes("<img src=\"x.png\" alt=\"y\"/>").Should().Be("src=\"x.png\" alt=\"y\"");

        // Act & Assert
        TagLister.ExtractAllAttributes("<br/>").Should().BeEmpty();

        // Act & Assert
        TagLister.ExtractAllAttributes("</p>").Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void RunsOnEveryCorpusXhtml_WithoutThrowing_AndFindsBody(string xhtmlPath)
    {
        // Arrange
        string source = File.ReadAllText(xhtmlPath);

        // Act
        TagLister lister = new(source);

        // Assert
        lister.Count.Should().BeGreaterThan(1);
        lister.FindBodyOpenTag().Should().BeGreaterThanOrEqualTo(0);
        lister.FindFirstNestingError().Should().BeNull();
    }

    /// <summary>
    /// Found in October 2026 on <c>tests/corpus/broken/markup</c>: <see cref="TagLister"/> is a scanner over the
    /// text, not a parser, so what a <c>&lt;style&gt;</c> or a <c>&lt;script&gt;</c> holds is read as markup too.
    /// A <c>&lt;p&gt;</c> written inside a CSS string therefore opens a tag that is never closed, and the
    /// <c>&lt;/style&gt;</c> that follows is reported as mismatched although the document is sound.
    ///
    /// This is wrong but harmless where it is used: <c>WellFormedChecker</c> asks for the nesting error only after
    /// <c>XmlReader</c> has already refused the file, to say where the trouble is — so the false report can only
    /// mislead the message of a file that is broken anyway. The test asserts today's behaviour; the day the scanner
    /// learns about raw-text elements, it will say so.
    /// </summary>
    [Fact]
    public void TagsWrittenInsideAStyleAreReadAsMarkup()
    {
        // Arrange
        const string source =
            "<html><body>\n"
            + "<style type=\"text/css\">p { content: \"<p>\"; }</style>\n"
            + "</body></html>\n";

        // Act
        NestingError? error = new TagLister(source).FindFirstNestingError();

        // Assert
        error.Should().NotBeNull();
        error!.Value.TagName.Should().Be("style", "the <p> inside the CSS was taken for an open tag");
    }

    [Fact]
    public void EnumerateOpeningTags_ListsTheBeginAndSelfClosingTagsOfTags()
    {
        // Arrange
        const string source =
            "<?xml version=\"1.0\"?>\n<!DOCTYPE html>\n<html><body>\n"
            + "<!-- <p class=\"in-comment\"> -->\n<![CDATA[ <p> ]]>\n"
            + "< p class=\"a\">x</p><br/><img src=\"y\" / ><broken <i>z</i>\n"
            + "</body></html>";

        // Act & Assert
        TagLister.EnumerateOpeningTags(source).Should().Equal(
            new TagLister(source).Tags
                .Where(t => t.Kind is TagKind.Begin or TagKind.SelfClosing)
                .Select(t => (t.Pos, t.Len)));

        // Act & Assert
        TagLister.EnumerateOpeningTags(source).Select(t => source.Substring(t.Pos, t.Len))
            .Should().Equal("<html>", "<body>", "< p class=\"a\">", "<br/>", "<img src=\"y\" / >", "<i>");
    }

    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void EnumerateOpeningTags_MatchesTheTagListOnEveryCorpusXhtml(string xhtmlPath)
    {
        // Arrange
        string source = File.ReadAllText(xhtmlPath);

        // Act & Assert
        TagLister.EnumerateOpeningTags(source).Should().Equal(
            new TagLister(source).Tags
                .Where(t => t.Kind is TagKind.Begin or TagKind.SelfClosing)
                .Select(t => (t.Pos, t.Len)));
    }
}
