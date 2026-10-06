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

    [Fact]
    public void Tags_AreListedInSourceOrder_WithPositionsAndKinds()
    {
        TagLister lister = new(Doc);

        lister.Tags[0].Should().Match<TagLister.TagInfo>(t =>
            t.TagName == "html" && t.Kind == TagKind.Begin && t.Pos == 0 && t.Len == 6);

        TagLister.TagInfo img = lister.Tags.Single(t => t.TagName == "img");
        img.Kind.Should().Be(TagKind.SelfClosing);
        Doc.Substring(img.Pos, img.Len).Should().Be("<img src=\"x.png\" alt=\"x\"/>");

        // The last real entry is followed by an end-of-list sentinel.
        lister.Tags[^1].Len.Should().Be(-1);
        lister.Tags[^1].Pos.Should().Be(-1);
    }

    [Fact]
    public void EndTags_LinkBackToTheirOpeningTag()
    {
        TagLister lister = new(Doc);

        int openP = Enumerable.Range(0, lister.Count).First(i => lister.At(i) is { TagName: "p", Kind: TagKind.Begin });
        int closeP = lister.FindCloseTagForOpen(openP);

        closeP.Should().BeGreaterThan(openP);
        lister.At(closeP).Kind.Should().Be(TagKind.End);
        lister.At(closeP).TagName.Should().Be("p");
        lister.At(closeP).OpenPos.Should().Be(lister.At(openP).Pos);
        lister.FindOpenTagForClose(closeP).Should().Be(openP);
    }

    [Fact]
    public void BodyTagsAndPositions_AreTracked()
    {
        TagLister lister = new(Doc);

        lister.FindBodyOpenTag().Should().BeGreaterThanOrEqualTo(0);
        lister.FindBodyCloseTag().Should().BeGreaterThan(lister.FindBodyOpenTag());

        int insideBody = Doc.IndexOf("Hello", StringComparison.Ordinal);
        int insideHead = Doc.IndexOf("<title>", StringComparison.Ordinal);

        lister.IsPositionInBody(insideBody).Should().BeTrue();
        lister.IsPositionInBody(insideHead).Should().BeFalse();
    }

    [Fact]
    public void PositionQueries_DistinguishOpenCloseAndText()
    {
        TagLister lister = new(Doc);

        int inOpenTag = Doc.IndexOf("id=\"a\"", StringComparison.Ordinal);
        int inCloseTag = Doc.IndexOf("</b>", StringComparison.Ordinal) + 2;
        int inText = Doc.IndexOf("bold", StringComparison.Ordinal);

        lister.IsPositionInTag(inOpenTag).Should().BeTrue();
        lister.IsPositionInOpenTag(inOpenTag).Should().BeTrue();
        lister.IsPositionInCloseTag(inOpenTag).Should().BeFalse();

        lister.IsPositionInCloseTag(inCloseTag).Should().BeTrue();
        lister.IsPositionInOpenTag(inCloseTag).Should().BeFalse();

        lister.IsPositionInTag(inText).Should().BeFalse();
    }

    [Fact]
    public void FindFirstAndLastTag_RelativeToPosition()
    {
        TagLister lister = new(Doc);
        int inText = Doc.IndexOf("bold", StringComparison.Ordinal);

        lister.At(lister.FindLastTagOnOrBefore(inText)).Should().Match<TagLister.TagInfo>(
            t => t.TagName == "b" && t.Kind == TagKind.Begin);
        lister.At(lister.FindFirstTagOnOrAfter(inText)).Should().Match<TagLister.TagInfo>(
            t => t.TagName == "b" && t.Kind == TagKind.End);
    }

    [Fact]
    public void GeneratePathToTag_ReturnsTagPathWithChildNumbers()
    {
        TagLister lister = new(Doc);
        int insideBold = Doc.IndexOf("bold", StringComparison.Ordinal);

        lister.GeneratePathToTag(insideBold).Should().StartWith("html").And.Contain("body").And.Contain("b ");
    }

    [Fact]
    public void MismatchedCloseTag_HasNoOpenPositionAndIsReportedAsNestingError()
    {
        TagLister lister = new("<root><a><b>x</a></b></root>");

        TagLister.TagInfo strayClose = lister.Tags.First(t => t.TagName == "a" && t.Kind == TagKind.End);
        strayClose.OpenPos.Should().Be(-1);

        NestingError? error = lister.FindFirstNestingError();
        error.Should().NotBeNull();
        error!.Value.TagName.Should().Be("a");
        error.Value.Pos.Should().Be(lister.Source.IndexOf("</a>", StringComparison.Ordinal));
    }

    [Fact]
    public void TruncatedDocument_ReportsOutermostUnclosedTag()
    {
        TagLister lister = new("<root><child>text");

        NestingError? error = lister.FindFirstNestingError();

        error.Should().NotBeNull();
        error!.Value.TagName.Should().Be("root");
        error.Value.Message.Should().Be(CoreStrings.Format("Nesting_UnclosedTag", "root"));
    }

    [Fact]
    public void WellNestedDocument_HasNoNestingError()
    {
        new TagLister(Doc).FindFirstNestingError().Should().BeNull();
    }

    [Fact]
    public void ParseAttribute_LocatesNameAndValueWithinTag()
    {
        TagLister.AttInfo info = TagLister.ParseAttribute("<p id=\"a\" class=\"lead\">", "class");

        info.AName.Should().Be("class");
        info.AValue.Should().Be("lead");
        "<p id=\"a\" class=\"lead\">".Substring(info.VPos, info.VLen).Should().Be("lead");
    }

    [Fact]
    public void ParseAttribute_MissingAttribute_ReturnsNone()
    {
        TagLister.ParseAttribute("<p id=\"a\">", "class").Should().Be(TagLister.AttInfo.None);
    }

    [Fact]
    public void SerializeAttribute_SwitchesToApostropheWhenValueHasDoubleQuote()
    {
        TagLister.SerializeAttribute("title", "plain").Should().Be("title=\"plain\"");
        TagLister.SerializeAttribute("title", "has \" quote").Should().Be("title='has \" quote'");
    }

    [Fact]
    public void ExtractAllAttributes_ReturnsAttributePortionWithoutTagNameOrSlash()
    {
        TagLister.ExtractAllAttributes("<img src=\"x.png\" alt=\"y\"/>").Should().Be("src=\"x.png\" alt=\"y\"");
        TagLister.ExtractAllAttributes("<br/>").Should().BeEmpty();
        TagLister.ExtractAllAttributes("</p>").Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void RunsOnEveryCorpusXhtml_WithoutThrowing_AndFindsBody(string xhtmlPath)
    {
        string source = File.ReadAllText(xhtmlPath);

        TagLister lister = new(source);

        lister.Count.Should().BeGreaterThan(1);
        lister.FindBodyOpenTag().Should().BeGreaterThanOrEqualTo(0);
        lister.FindFirstNestingError().Should().BeNull();
    }

    [Fact]
    public void EnumerateOpeningTags_ListsTheBeginAndSelfClosingTagsOfTags()
    {
        const string source =
            "<?xml version=\"1.0\"?>\n<!DOCTYPE html>\n<html><body>\n"
            + "<!-- <p class=\"in-comment\"> -->\n<![CDATA[ <p> ]]>\n"
            + "< p class=\"a\">x</p><br/><img src=\"y\" / ><broken <i>z</i>\n"
            + "</body></html>";

        TagLister.EnumerateOpeningTags(source).Should().Equal(
            new TagLister(source).Tags
                .Where(t => t.Kind is TagKind.Begin or TagKind.SelfClosing)
                .Select(t => (t.Pos, t.Len)));
        TagLister.EnumerateOpeningTags(source).Select(t => source.Substring(t.Pos, t.Len))
            .Should().Equal("<html>", "<body>", "< p class=\"a\">", "<br/>", "<img src=\"y\" / >", "<i>");
    }

    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void EnumerateOpeningTags_MatchesTheTagListOnEveryCorpusXhtml(string xhtmlPath)
    {
        string source = File.ReadAllText(xhtmlPath);

        TagLister.EnumerateOpeningTags(source).Should().Equal(
            new TagLister(source).Tags
                .Where(t => t.Kind is TagKind.Begin or TagKind.SelfClosing)
                .Select(t => (t.Pos, t.Len)));
    }
}
