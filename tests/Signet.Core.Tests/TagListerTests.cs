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
        string malformedDir = $"{Path.DirectorySeparatorChar}malformed{Path.DirectorySeparatorChar}";
        TheoryData<string> data = new();
        foreach (string path in Directory.EnumerateFiles(CorpusPaths.Root, "*.xhtml", SearchOption.AllDirectories))
        {
            if (!path.Contains(malformedDir, StringComparison.Ordinal))
            {
                data.Add(path);
            }
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
