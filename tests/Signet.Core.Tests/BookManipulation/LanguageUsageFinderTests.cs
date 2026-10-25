using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="LanguageUsageFinder"/> — the places of a book that state a language in so many words, as
/// against the ones that inherit it.
/// </summary>
public sealed class LanguageUsageFinderTests
{
    private const string Chapter = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml">
        <body lang="pl-PL" xml:lang="pl-PL">
          <p lang="pl-PL">one</p>
          <p lang="de-DE">zwei</p>
          <p>three</p>
          <p lang="PL-pl">four</p>
        </body>
        </html>
        """;

    private const string Stylesheet = "p:lang(pl-PL) { color: red; }";

    private static IReadOnlyList<ClassUsageSource> Sources() =>
    [
        new ClassUsageSource("EPUB/styles/style.css", Stylesheet, IsStyleSheet: true),
        new ClassUsageSource("EPUB/text/chapter1.xhtml", Chapter, IsStyleSheet: false),
    ];

    [Fact]
    public void Every_place_that_states_the_language_is_found_and_nothing_that_only_inherits_it()
    {
        // Act
        IReadOnlyList<ClassUsage> usages = LanguageUsageFinder.Find("pl-PL", Sources());

        // Assert
        usages.Should().HaveCount(4, "body states it twice, two paragraphs once each");
        usages.Should().OnlyContain(u => u.BookPath.EndsWith("chapter1.xhtml", StringComparison.Ordinal));
        usages.Should().OnlyContain(u => u.Kind == ClassUsageKind.LanguageAttribute);
        usages.Select(u => u.Line).Should().BeInAscendingOrder();
        usages.Select(u => u.Context).Should().OnlyContain(c => c.Length > 0);
    }

    [Fact]
    public void A_language_tag_is_matched_whatever_its_case()
    {
        // Act — BCP 47 language tags are case-insensitive, so pl-PL and PL-pl are one language.
        IReadOnlyList<ClassUsage> usages = LanguageUsageFinder.Find("pl-pl", Sources());

        // Assert
        usages.Should().HaveCount(4);
    }

    [Fact]
    public void Another_language_finds_only_its_own_places()
    {
        // Act
        IReadOnlyList<ClassUsage> usages = LanguageUsageFinder.Find("de-DE", Sources());

        // Assert
        usages.Should().ContainSingle();
        usages[0].Context.Should().Contain("zwei");
    }

    [Fact]
    public void An_empty_language_finds_every_place_that_states_one()
    {
        // Act
        IReadOnlyList<ClassUsage> usages = LanguageUsageFinder.Find(string.Empty, Sources());

        // Assert
        usages.Should().HaveCount(5, "the German paragraph as well");
    }

    /// <summary>A stylesheet states no language of its own, so :lang() is not a place that sets one.</summary>
    [Fact]
    public void A_stylesheet_is_not_searched()
    {
        // Act
        IReadOnlyList<ClassUsage> usages = LanguageUsageFinder.Find(
            "pl-PL",
            [new ClassUsageSource("EPUB/styles/style.css", Stylesheet, IsStyleSheet: true)]);

        // Assert
        usages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("lang=\"pl-PL\"", 6, "pl-PL")]
    [InlineData("xml:lang=\"de-DE\"", 11, "de-DE")]
    public void The_language_under_the_caret_is_the_value_of_the_attribute_it_is_in(string attribute, int caretInValue, string expected)
    {
        // Arrange
        string text = "<p " + attribute + ">x</p>";
        int caret = text.IndexOf(attribute, StringComparison.Ordinal) + caretInValue;

        // Act & Assert
        LanguageUsageFinder.FindLanguageAtCaret(text, caret).Should().Be(expected);
    }

    [Fact]
    public void There_is_no_language_under_a_caret_outside_a_language_attribute()
    {
        // Arrange
        const string Text = "<p class=\"a\" lang=\"pl-PL\">text</p>";

        // Act & Assert
        LanguageUsageFinder.FindLanguageAtCaret(Text, Text.IndexOf("class", StringComparison.Ordinal) + 2).Should().BeNull();
        LanguageUsageFinder.FindLanguageAtCaret(Text, Text.IndexOf("text", StringComparison.Ordinal)).Should().BeNull();
        LanguageUsageFinder.FindLanguageAtCaret(string.Empty, 0).Should().BeNull();
    }
}
