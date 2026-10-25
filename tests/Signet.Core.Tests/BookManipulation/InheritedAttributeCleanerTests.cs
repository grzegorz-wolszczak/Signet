using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="InheritedAttributeCleaner"/> — finding and removing the attributes that only repeat a value
/// the element already has from an ancestor.
/// </summary>
public sealed class InheritedAttributeCleanerTests
{
    private static string Page(string bodyAttributes, string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head><title>t</title></head>\n<body" + bodyAttributes + ">\n" + body + "\n</body>\n</html>\n";

    private static string RemoveAll(string text) =>
        InheritedAttributeCleaner.Remove(text, InheritedAttributeCleaner.Find(text).Select(a => (a.TagPos, a.Name)));

    [Fact]
    public void A_language_repeated_from_an_ancestor_is_found_with_the_element_it_comes_from()
    {
        // Arrange
        string text = Page(" lang=\"pl-PL\"", "<p lang=\"pl-PL\">x</p>");

        // Act
        RedundantAttribute attribute = InheritedAttributeCleaner.Find(text).Should().ContainSingle().Subject;

        // Assert
        attribute.Name.Should().Be("lang");
        attribute.Value.Should().Be("pl-PL");
        attribute.TagName.Should().Be("p");
        attribute.FromTagName.Should().Be("body");
    }

    /// <summary>The attribute of the element highest up stays: it is the one the others inherit from.</summary>
    [Fact]
    public void The_topmost_declaration_is_never_redundant()
    {
        // Arrange
        string text = Page(" lang=\"pl-PL\" xml:lang=\"pl-PL\"", "<p lang=\"pl-PL\">x</p>");

        // Act
        string cleaned = RemoveAll(text);

        // Assert
        cleaned.Should().Contain("<body lang=\"pl-PL\" xml:lang=\"pl-PL\">");
        cleaned.Should().Contain("<p>x</p>");
    }

    [Fact]
    public void A_descendant_with_a_language_of_its_own_is_left_alone_and_so_is_what_follows_it()
    {
        // Arrange — pl inside de inside pl: the innermost repeats its grandparent, not its parent.
        string text = Page(" lang=\"pl-PL\"", "<div lang=\"de-DE\"><p lang=\"pl-PL\">x</p><span lang=\"de-DE\">y</span></div>");

        // Act
        var found = InheritedAttributeCleaner.Find(text);

        // Assert
        found.Should().ContainSingle("the span repeats the div it sits in").Which.TagName.Should().Be("span");
        RemoveAll(text).Should().Contain("<p lang=\"pl-PL\">x</p>").And.Contain("<span>y</span>");
    }

    [Fact]
    public void A_language_that_differs_only_in_case_counts_as_the_same_one()
    {
        // Arrange — BCP 47 language tags are case-insensitive.
        string text = Page(" lang=\"pl-PL\"", "<p lang=\"PL-pl\">x</p>");

        // Act & Assert
        InheritedAttributeCleaner.Find(text).Should().ContainSingle();
    }

    [Fact]
    public void Lang_and_xml_lang_are_weighed_one_by_one()
    {
        // Arrange — the element repeats the language in lang, but states another one in xml:lang.
        string text = Page(" lang=\"pl-PL\" xml:lang=\"pl-PL\"", "<p lang=\"pl-PL\" xml:lang=\"de-DE\">x</p>");

        // Act
        var found = InheritedAttributeCleaner.Find(text);

        // Assert
        found.Select(a => a.Name).Should().Equal("lang");
        RemoveAll(text).Should().Contain("<p xml:lang=\"de-DE\">x</p>");
    }

    [Fact]
    public void An_empty_value_is_never_redundant()
    {
        // Arrange — lang="" deliberately says "no language", whatever the ancestor says.
        string text = Page(" lang=\"pl-PL\"", "<p lang=\"\">x</p>");

        // Act & Assert
        InheritedAttributeCleaner.Find(text).Should().BeEmpty();
    }

    [Theory]
    [InlineData("rtl", "rtl", 1)]
    [InlineData("rtl", "ltr", 0)]
    [InlineData("auto", "auto", 0)]
    [InlineData("ltr", "auto", 0)]
    public void Dir_repeats_like_a_language_except_for_auto(string outer, string inner, int expected)
    {
        // Arrange — dir="auto" works the direction out from the element's own text, so it is never inherited-equal.
        string text = Page(" dir=\"" + outer + "\"", "<p dir=\"" + inner + "\">x</p>");

        // Act & Assert
        InheritedAttributeCleaner.Find(text).Should().HaveCount(expected);
    }

    [Fact]
    public void A_namespace_declared_again_with_the_same_uri_is_redundant_and_a_different_one_is_not()
    {
        // Arrange
        string text = Page(
            string.Empty,
            "<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect xmlns=\"http://www.w3.org/2000/svg\" /></svg>");

        // Act
        var found = InheritedAttributeCleaner.Find(text);

        // Assert
        found.Select(a => a.TagName).Should().Equal("div", "rect");
        found.Should().OnlyContain(a => a.Name == "xmlns");
    }

    /// <summary>A URI is case-sensitive, so the same namespace written differently is not the same declaration.</summary>
    [Fact]
    public void A_namespace_uri_is_compared_exactly()
    {
        // Arrange
        string text = Page(string.Empty, "<div xmlns=\"http://www.w3.org/1999/XHTML\">x</div>");

        // Act & Assert
        InheritedAttributeCleaner.Find(text).Should().BeEmpty();
    }

    [Fact]
    public void Attributes_that_do_not_inherit_are_never_touched()
    {
        // Arrange
        string text = Page(
            " class=\"c\" id=\"top\" style=\"color: red\" title=\"t\"",
            "<p class=\"c\" style=\"color: red\" title=\"t\" role=\"doc-part\">x</p>");

        // Act & Assert
        InheritedAttributeCleaner.Find(text).Should().BeEmpty();
        RemoveAll(text).Should().Be(text);
    }

    [Theory]
    [InlineData("<p lang=\"pl-PL\">x</p>", "<p>x</p>")]
    [InlineData("<p class=\"c\" lang=\"pl-PL\">x</p>", "<p class=\"c\">x</p>")]
    [InlineData("<p lang=\"pl-PL\" class=\"c\">x</p>", "<p class=\"c\">x</p>")]
    [InlineData("<br lang=\"pl-PL\" />", "<br />")]
    [InlineData("<p\n   lang=\"pl-PL\">x</p>", "<p>x</p>")]
    public void Removing_an_attribute_takes_the_white_space_before_it_and_leaves_the_rest_of_the_tag(string body, string expected)
    {
        // Arrange
        string text = Page(" lang=\"pl-PL\"", body);

        // Act & Assert
        RemoveAll(text).Should().Contain(expected);
    }

    [Fact]
    public void Removing_is_idempotent()
    {
        // Arrange
        string text = Page(" lang=\"pl-PL\" xml:lang=\"pl-PL\"", "<div lang=\"pl-PL\"><p lang=\"pl-PL\" xml:lang=\"pl-PL\">x</p></div>");

        // Act
        string once = RemoveAll(text);
        string twice = RemoveAll(once);

        // Assert
        once.Should().NotBe(text);
        twice.Should().Be(once);
        InheritedAttributeCleaner.Find(once).Should().BeEmpty();
    }

    [Fact]
    public void Only_the_attributes_asked_for_are_removed()
    {
        // Arrange
        string text = Page(" lang=\"pl-PL\"", "<p lang=\"pl-PL\">x</p><span lang=\"pl-PL\">y</span>");
        RedundantAttribute first = InheritedAttributeCleaner.Find(text)[0];

        // Act
        string cleaned = InheritedAttributeCleaner.Remove(text, new[] { (first.TagPos, first.Name) });

        // Assert
        cleaned.Should().Contain("<p>x</p>").And.Contain("<span lang=\"pl-PL\">y</span>");
    }
}
