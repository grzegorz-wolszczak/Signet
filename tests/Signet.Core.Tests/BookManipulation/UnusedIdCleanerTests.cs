using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="UnusedIdCleaner"/> — finding and removing <c>id</c> attributes.</summary>
public sealed class UnusedIdCleanerTests
{
    private static string Page(string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" id=\"root\">\n"
        + "<head><title>t</title></head>\n<body id=\"top\">\n" + body + "\n</body>\n</html>\n";

    private static string RemoveAll(string text) => UnusedIdCleaner.Remove(text, UnusedIdCleaner.Find(text).Select(a => a.TagPos));

    [Fact]
    public void Only_ids_of_elements_in_the_body_are_found()
    {
        // Arrange
        string text = Page("<p id=\"a\">x</p><br id='b' /><span data-id=\"c\" title=\"id='d'\">y</span><p id=\"\">z</p>");

        // Act
        var found = UnusedIdCleaner.Find(text);

        // Assert
        found.Select(a => a.Value).Should().Equal("a", "b");
        found.Select(a => a.TagName).Should().Equal("p", "br");
    }

    [Fact]
    public void An_id_is_found_with_its_character_references_decoded()
    {
        // Arrange
        string text = Page("<p id=\"a&amp;b\">x</p>");

        // Act & Assert
        UnusedIdCleaner.Find(text).Single().Value.Should().Be("a&b");
    }

    [Theory]
    [InlineData("<p id=\"a\">x</p>", "<p>x</p>")]
    [InlineData("<p id=\"a\" class=\"c\">x</p>", "<p class=\"c\">x</p>")]
    [InlineData("<p class=\"c\" id=\"a\" lang=\"en\">x</p>", "<p class=\"c\" lang=\"en\">x</p>")]
    [InlineData("<p class=\"c\" id=\"a\">x</p>", "<p class=\"c\">x</p>")]
    [InlineData("<p class=\"c\" id = 'a' >x</p>", "<p class=\"c\" >x</p>")]
    [InlineData("<br id=\"a\" />", "<br />")]
    [InlineData("<br id=\"a\"/>", "<br/>")]
    [InlineData("<p\n   id=\"a\"\n   class=\"c\">x</p>", "<p\n   class=\"c\">x</p>")]
    public void Removing_takes_the_attribute_and_the_blank_before_it_and_leaves_the_rest_of_the_tag(string body, string expected)
    {
        // Arrange
        string text = Page(body);

        // Act
        string result = RemoveAll(text);

        // Assert
        result.Should().Be(Page(expected));
    }

    [Fact]
    public void Removing_touches_only_the_chosen_elements_and_never_the_html_or_body_tags()
    {
        // Arrange
        string text = Page("<p id=\"a\">x</p><p id=\"b\">y</p>");
        int first = UnusedIdCleaner.Find(text).First().TagPos;

        // Act
        string result = UnusedIdCleaner.Remove(text, new[] { first });

        // Assert
        result.Should().Be(Page("<p>x</p><p id=\"b\">y</p>"));
    }

    [Fact]
    public void Text_that_only_looks_like_an_id_is_left_alone()
    {
        // Arrange
        string text = Page("<p title=\"x id='a'\">id=\"b\"</p>");

        // Act
        var found = UnusedIdCleaner.Find(text);

        // Assert
        found.Should().BeEmpty();
        RemoveAll(text).Should().Be(text);
    }
}
