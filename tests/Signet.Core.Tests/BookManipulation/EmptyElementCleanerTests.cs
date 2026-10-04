using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="EmptyElementCleaner"/> — finding and removing empty p/span/div elements.</summary>
public sealed class EmptyElementCleanerTests
{
    private static string Page(string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head><title>t</title></head>\n<body>\n" + body + "\n</body>\n</html>\n";

    private static string RemoveAll(string text) =>
        EmptyElementCleaner.Remove(text, EmptyElementCleaner.Find(text).Select(e => e.Pos));

    [Theory]
    [InlineData("<p class=\"calibre8\"></p>", "p")]
    [InlineData("<p/>", "p")]
    [InlineData("<div>  \n  </div>", "div")]
    [InlineData("<p>x<span class=\"s\"></span>y</p>", "span")]
    public void Elements_without_content_or_with_only_whitespace_are_empty(string body, string tagName)
    {
        EmptyElementCleaner.Find(Page(body)).Should().ContainSingle().Which.TagName.Should().Be(tagName);
    }

    [Theory]
    [InlineData("<p>&#160;</p>")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<p> </p>")]
    [InlineData("<p><!-- c --></p>")]
    [InlineData("<p><br/></p>")]
    [InlineData("<p id=\"ch3\"></p>")]
    [InlineData("<section></section>")]
    public void Content_entities_comments_children_and_ids_keep_an_element(string body)
    {
        EmptyElementCleaner.Find(Page(body)).Should().BeEmpty();
    }

    [Fact]
    public void A_block_alone_on_its_line_is_removed_with_the_line()
    {
        string text = Page("<p>a</p>\n  <p class=\"calibre8\"></p>\n<p>b</p>");

        RemoveAll(text).Should().Be(Page("<p>a</p>\n<p>b</p>"));
    }

    [Fact]
    public void A_block_sharing_its_line_is_removed_alone()
    {
        string text = Page("<p>a</p><div></div><p>b</p>");

        RemoveAll(text).Should().Be(Page("<p>a</p><p>b</p>"));
    }

    [Fact]
    public void An_empty_span_keeps_its_space()
    {
        string text = Page("<p>word<span class=\"s\"> </span>word</p>");

        RemoveAll(text).Should().Be(Page("<p>word word</p>"));
    }

    [Fact]
    public void Only_the_requested_elements_are_removed()
    {
        string text = Page("<p class=\"a\"></p>\n<p class=\"b\"></p>");
        int second = EmptyElementCleaner.Find(text)[1].Pos;

        EmptyElementCleaner.Remove(text, new[] { second }).Should().Be(Page("<p class=\"a\"></p>"));
    }
}
