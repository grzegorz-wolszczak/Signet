using System.Collections.Generic;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>Tests of <see cref="MarkupTokenizer"/> — a lenient tag scanner.</summary>
public sealed class MarkupTokenizerTests
{
    private static List<MarkupToken> TokenizeAll(string source)
    {
        MarkupTokenizer tokenizer = new(source);
        List<MarkupToken> tokens = new();
        while (tokenizer.ParseNext() is { } token)
        {
            tokens.Add(token);
        }

        return tokens;
    }

    [Fact]
    public void Splits_text_and_tags_and_tracks_the_tag_path()
    {
        List<MarkupToken> tokens = TokenizeAll("<a><b>hello</b></a>");

        tokens.Should().HaveCount(5);
        tokens[0].TagName.Should().Be("a");
        tokens[0].TagType.Should().Be(MarkupTokenType.Begin);
        tokens[1].TagName.Should().Be("b");
        tokens[1].TagPath.Should().Be("root.a.b");
        tokens[2].Text.Should().Be("hello");
        tokens[2].TagPath.Should().Be("root.a.b");
        tokens[3].TagType.Should().Be(MarkupTokenType.End);
        tokens[4].TagType.Should().Be(MarkupTokenType.End);
    }

    [Fact]
    public void Parses_attributes_keeping_order_and_raw_values()
    {
        List<MarkupToken> tokens = TokenizeAll("<item id=\"x\" href='a/b.xhtml' properties=\"nav scripted\"/>");

        MarkupToken item = tokens.Should().ContainSingle().Subject;
        item.TagType.Should().Be(MarkupTokenType.Single);
        item.Attributes.Keys.Should().Equal("id", "href", "properties");
        item.Attributes.Value("href").Should().Be("a/b.xhtml");
        item.Attributes.Value("properties").Should().Be("nav scripted");
    }

    [Fact]
    public void Recognizes_xml_header_comment_and_cdata()
    {
        List<MarkupToken> tokens = TokenizeAll("<?xml version=\"1.0\"?><!-- note --><![CDATA[ raw ]]>");

        tokens[0].TagType.Should().Be(MarkupTokenType.XmlHeader);
        tokens[1].TagType.Should().Be(MarkupTokenType.Comment);
        tokens[1].Attributes.Value("special").Should().Be(" note ");
        tokens[2].TagType.Should().Be(MarkupTokenType.CData);
        tokens[2].Attributes.Value("special").Should().Be(" raw ");
    }

    [Fact]
    public void Tolerates_an_unclosed_tag_by_treating_it_as_text()
    {
        // no '>' before the next '<' — the fragment is treated as text
        List<MarkupToken> tokens = TokenizeAll("<manifest><item id=\"a\"<spine>");

        tokens[0].TagName.Should().Be("manifest");
        tokens[1].Text.Should().Be("<item id=\"a\"");
        tokens[2].TagName.Should().Be("spine");
    }

    [Fact]
    public void Detects_a_prefixed_package_element_and_reports_namespace_remap()
    {
        MarkupTokenizer tokenizer = new("<opf:package version=\"3.0\"><opf:metadata/></opf:package>");

        MarkupToken package = tokenizer.ParseNext()!;
        package.TagName.Should().Be("package");
        tokenizer.NsRemapNeeded.Should().BeTrue();
        tokenizer.OldPrefix.Should().Be("opf");

        MarkupToken metadata = tokenizer.ParseNext()!;
        metadata.TagName.Should().Be("metadata");
    }

    [Fact]
    public void Empty_source_yields_no_tokens()
    {
        TokenizeAll(string.Empty).Should().BeEmpty();
    }
}
