using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Tests of <see cref="EpubAnonymizer"/>. The fixture books under <c>tests/corpus/real</c> are made with it, so what
/// it keeps is what those books can still test: get this wrong and the fixtures quietly stop covering the thing they
/// were taken from. Hence a test per promise rather than one test of the whole output.
/// </summary>
public sealed class EpubAnonymizerTests
{
    private const string Page =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"pl\" xml:lang=\"pl\">\n"
        + "<head><title>Księga Rodzaju – rozdział 1</title></head>\n<body>\n"
        + "<section class=\"chapter-body\" epub:type=\"chapter\">\n"
        + "<span id=\"w1\" class=\"verse-num\">1</span> Na początku Bóg stworzył niebo i ziemię"
        + "<sup class=\"fnref\"><a href=\"#fn12\" id=\"ref12\" epub:type=\"noteref\">12</a></sup>.\n"
        + "</section>\n"
        + "<aside id=\"fn12\" epub:type=\"footnote\" class=\"footnote\">\n"
        + "  <a href=\"#ref12\" class=\"fn-back\">[12]</a> Por. Wj 24,12 &amp; Ez 11,19 &#8212; zob. tam.\n"
        + "</aside>\n</body>\n</html>\n";

    [Fact]
    public void Every_tag_and_attribute_stays_exactly_as_it_was()
    {
        // Act
        string result = EpubAnonymizer.Anonymize(Page);

        // Assert
        Tags(result).Should().Equal(Tags(Page));
    }

    [Fact]
    public void The_ids_and_the_links_stay_so_the_footnotes_still_pair_up()
    {
        // Act
        string result = EpubAnonymizer.Anonymize(Page);

        // Assert
        result.Should().Contain("id=\"fn12\"").And.Contain("href=\"#fn12\"")
            .And.Contain("id=\"ref12\"").And.Contain("href=\"#ref12\"")
            .And.Contain("epub:type=\"noteref\"").And.Contain("epub:type=\"footnote\"")
            .And.Contain("class=\"fnref\"").And.Contain("class=\"verse-num\"")
            .And.Contain("lang=\"pl\"");
    }

    /// <summary>
    /// The digits and the punctuation of the text are what a marker is made of, and the conversion reads markers, so
    /// they have to survive: <c>[12]</c> stays <c>[12]</c> and the verse number stays a number.
    /// </summary>
    [Fact]
    public void The_markers_and_the_numbers_of_the_text_stay()
    {
        // Act
        string result = EpubAnonymizer.Anonymize(Page);

        // Assert
        result.Should().Contain(">[12]</a>", "the marker of the link back")
            .And.Contain(">12</a>", "the marker of the reference")
            .And.Contain(">1</span>", "the verse number")
            .And.Contain("24,12").And.Contain("11,19");
    }

    [Fact]
    public void The_entity_references_stay()
    {
        // Act
        string result = EpubAnonymizer.Anonymize(Page);

        // Assert
        result.Should().Contain("&amp;").And.Contain("&#8212;");
    }

    [Fact]
    public void The_prose_is_gone_including_the_letters_outside_ascii()
    {
        // Act
        string result = EpubAnonymizer.Anonymize(Page);

        // Assert
        foreach (string word in new[] { "początku", "stworzył", "niebo", "ziemię", "Księga", "Rodzaju", "rozdział", "zob", "tam" })
        {
            result.Should().NotContain(word, "the word \"{0}\" is the author's", word);
        }

        result.Should().MatchRegex("[a-z]{3}", "the prose is replaced, not removed");
        result.Where(char.IsLetter).Should().OnlyContain(c => c < 128, "no letter of the source survives");
    }

    [Fact]
    public void A_word_keeps_its_length_and_its_leading_capital()
    {
        // Act
        string result = EpubAnonymizer.Anonymize("<p>Abcde fg hijklmnop.</p>");

        // Assert
        Match match = Regex.Match(result, "<p>(\\S+) (\\S+) (\\S+)</p>");
        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().HaveLength(5).And.MatchRegex("^[A-Z][a-z]{4}$");
        match.Groups[2].Value.Should().HaveLength(2);
        match.Groups[3].Value.Should().HaveLength(10, "nine letters and the full stop");
    }

    /// <summary>A URN or an address is left alone: the OPF's identifier has to stay an identifier.</summary>
    [Fact]
    public void An_identifier_or_an_address_is_left_alone()
    {
        // Act
        string result = EpubAnonymizer.Anonymize(
            "<dc:identifier id=\"BookId\">urn:uuid:64df7cef-a5f6-49e0-a0dd-ede7871bd424</dc:identifier>\n"
            + "<p>Zobacz https://example.org/strona oraz ktos@example.org.</p>");

        // Assert
        result.Should().Contain("urn:uuid:64df7cef-a5f6-49e0-a0dd-ede7871bd424")
            .And.Contain("https://example.org/strona").And.Contain("ktos@example.org");
    }

    [Fact]
    public void A_comment_is_left_alone_so_a_commented_out_tag_stays_commented_out()
    {
        // Act
        string result = EpubAnonymizer.Anonymize("<p>Tekst</p><!-- <aside id=\"fn1\">uwaga</aside> -->");

        // Assert
        result.Should().Contain("<!-- <aside id=\"fn1\">uwaga</aside> -->");
    }

    [Fact]
    public void The_same_input_gives_the_same_output()
    {
        // Act & Assert
        EpubAnonymizer.Anonymize(Page).Should().Be(EpubAnonymizer.Anonymize(Page));
    }

    // Every tag of the markup, in order, as written.
    private static List<string> Tags(string text) =>
        Regex.Matches(text, "<[^>]*>", RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Select(m => m.Value).ToList();
}
