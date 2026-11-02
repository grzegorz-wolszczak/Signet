using System;
using System.Collections.Generic;
using AutoFixture;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Hyphenation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="SoftHyphenInserter"/>: adding soft hyphens by the patterns of the language of the text, and
/// removing them from chosen languages and places.
/// </summary>
public sealed class SoftHyphenInserterTests
{
    private const string Shy = SoftHyphenInserter.SoftHyphenEntity;

    private static readonly Fixture Any = new();

    private static readonly IReadOnlySet<int> NoPlaces = new HashSet<int>();

    private static string Page(string body, string htmlLang = "en") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE html><html xmlns=\"http://www.w3.org/1999/xhtml\""
        + (htmlLang.Length > 0 ? $" lang=\"{htmlLang}\"" : string.Empty)
        + "><head><title>Title</title></head><body>" + body + "</body></html>";

    // English by Liang's patterns, Polish before every "c", nothing else.
    private static HyphenationPatterns? EnglishAndPolish(string key) =>
        TestHyphenation.EnglishAndPolish.TryGetValue(key, out HyphenationPatterns? patterns) ? patterns : null;

    private static string Add(string html, string defaultLanguage = "", int minWordLength = SoftHyphenInserter.DefaultMinWordLength) =>
        SoftHyphenInserter.InsertSoftHyphens(html, defaultLanguage, EnglishAndPolish, minWordLength);

    [Fact]
    public void A_word_is_broken_where_the_patterns_of_its_language_allow()
    {
        // Arrange
        string html = Page("<p>hyphenation</p>");

        // Act
        string result = Add(html);

        // Assert
        result.Should().Be(Page($"<p>hy{Shy}phen{Shy}ation</p>"));
    }

    [Fact]
    public void Each_language_is_broken_by_its_own_patterns()
    {
        // Arrange
        string html = Page("<p>hyphenation <span lang=\"pl\">cierpliwości</span></p>");

        // Act
        string result = Add(html);

        // Assert
        result.Should().Be(Page($"<p>hy{Shy}phen{Shy}ation <span lang=\"pl\">cierpliwoś{Shy}ci</span></p>"));
    }

    [Fact]
    public void A_language_without_patterns_is_not_hyphenated()
    {
        // Arrange
        string html = Page("<p lang=\"de\">hyphenation</p>");

        // Act
        string result = Add(html);

        // Assert
        result.Should().Be(html, "no language-independent guess is made");
    }

    [Fact]
    public void Text_no_element_states_a_language_for_takes_the_language_of_the_book()
    {
        // Arrange
        string html = Page("<p>hyphenation</p>", htmlLang: string.Empty);

        // Act
        string withBookLanguage = Add(html, defaultLanguage: "en");
        string withoutAny = Add(html, defaultLanguage: string.Empty);

        // Assert
        withBookLanguage.Should().Contain($"hy{Shy}phen{Shy}ation");
        withoutAny.Should().Be(html, "the language is unknown");
    }

    [Theory]
    [InlineData("lang=\"EN\"")]
    [InlineData("lang=\"en_US\"", "en-us")]
    [InlineData("xml:lang=\"en\"")]
    [InlineData("lang=\"en\" xml:lang=\"pl\"")]
    public void The_language_is_read_from_lang_or_xml_lang_without_regard_to_case(string attributes, string key = "en")
    {
        // Arrange
        string html = Page($"<p {attributes}>hyphenation</p>", htmlLang: string.Empty);
        List<string> asked = new();

        // Act
        string result = SoftHyphenInserter.InsertSoftHyphens(html, string.Empty, k =>
        {
            asked.Add(k);
            return TestHyphenation.Liang;
        });

        // Assert
        asked.Should().Contain(key);
        result.Should().Contain($"hy{Shy}phen{Shy}ation");
    }

    [Fact]
    public void Words_shorter_than_the_minimum_are_left_alone()
    {
        // Arrange
        string html = Page("<p>hyphenation</p>");

        // Act
        string result = Add(html, minWordLength: "hyphenation".Length + 1);

        // Assert
        result.Should().Be(html);
    }

    [Theory]
    [InlineData("&shy;")]
    [InlineData("&#173;")]
    [InlineData("&#xAD;")]
    [InlineData("­")]
    public void A_word_with_a_soft_hyphen_in_any_form_is_left_as_it_is(string softHyphen)
    {
        // Arrange
        string html = Page($"<p>hyphe{softHyphen}nation</p>");

        // Act
        string result = Add(html);

        // Assert
        result.Should().Be(html);
    }

    [Fact]
    public void Adding_twice_adds_nothing_the_second_time()
    {
        // Arrange
        string once = Add(Page("<p>hyphenation and cierpliwości</p>"));

        // Act
        string twice = Add(once);

        // Assert
        twice.Should().Be(once);
    }

    [Theory]
    [InlineData("<pre>hyphenation</pre>")]
    [InlineData("<p><code>hyphenation</code></p>")]
    [InlineData("<script>var hyphenation;</script>")]
    [InlineData("<style>.hyphenation{}</style>")]
    [InlineData("<p title=\"hyphenation\">x</p>")]
    [InlineData("<!-- hyphenation -->")]
    public void Nothing_is_added_outside_the_text_of_a_language(string body)
    {
        // Arrange
        string html = Page(body);

        // Act
        string result = Add(html);

        // Assert
        result.Should().Be(html);
    }

    [Fact]
    public void The_head_gets_no_soft_hyphen()
    {
        // Arrange
        string html = Page("<p>x</p>").Replace("<title>Title</title>", "<title>hyphenation</title>", StringComparison.Ordinal);

        // Act
        string result = Add(html);

        // Assert
        result.Should().Be(html);
    }

    [Fact]
    public void An_entity_reference_ends_a_word_and_is_kept_whole()
    {
        // Arrange
        string html = Page("<p>hyphenation&amp;hyphenation &#233;</p>");

        // Act
        string result = Add(html);

        // Assert
        result.Should().Be(Page($"<p>hy{Shy}phen{Shy}ation&amp;hy{Shy}phen{Shy}ation &#233;</p>"));
    }

    [Fact]
    public void Removing_takes_the_soft_hyphens_of_the_chosen_languages_only()
    {
        // Arrange
        string html = Page($"<p>hy{Shy}phen <span lang=\"pl\">cierpliwoś{Shy}ci</span></p>");

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(html, string.Empty, key => key == "en", NoPlaces);

        // Assert
        result.Should().Be(Page($"<p>hyphen <span lang=\"pl\">cierpliwoś{Shy}ci</span></p>"));
    }

    [Fact]
    public void Removing_takes_every_form_of_a_soft_hyphen()
    {
        // Arrange
        string html = Page("<p>a&shy;b&#173;c&#xAD;d­e&#0173;f</p>");

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(html, string.Empty, _ => true, NoPlaces);

        // Assert
        result.Should().Be(Page("<p>abcdef</p>"));
    }

    [Fact]
    public void Removing_takes_from_outside_the_text_only_the_places_chosen()
    {
        // Arrange
        string html = Page($"<p><a href=\"x{Shy}y\" title=\"t{Shy}u\">l{Shy}ink</a></p><pre>p{Shy}re</pre>");
        int inHref = html.IndexOf("x" + Shy, StringComparison.Ordinal) + 1;

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(html, string.Empty, _ => false, new HashSet<int> { inHref });

        // Assert
        result.Should().Be(Page($"<p><a href=\"xy\" title=\"t{Shy}u\">l{Shy}ink</a></p><pre>p{Shy}re</pre>"));
    }

    [Fact]
    public void Removing_from_the_text_leaves_the_places_outside_it()
    {
        // Arrange
        string html = Page($"<p title=\"t{Shy}u\">l{Shy}ink</p><!-- c{Shy}d -->");

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(html, string.Empty, _ => true, NoPlaces);

        // Assert
        result.Should().Be(Page($"<p title=\"t{Shy}u\">link</p><!-- c{Shy}d -->"));
    }

    [Fact]
    public void The_unknown_language_has_the_empty_key()
    {
        // Arrange
        string word = Any.Create<string>()[..8];
        string html = Page($"<p>{word[..4]}{Shy}{word[4..]}</p>", htmlLang: string.Empty);

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(html, string.Empty, key => key.Length == 0, NoPlaces);

        // Assert
        result.Should().Be(Page($"<p>{word}</p>", htmlLang: string.Empty));
    }

    [Fact]
    public void Adding_then_removing_gives_the_file_back()
    {
        // Arrange
        string html = Page("<p>hyphenation <span lang=\"pl\">cierpliwości</span></p>");

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(Add(html), string.Empty, _ => true, NoPlaces);

        // Assert
        result.Should().Be(html);
    }
}
