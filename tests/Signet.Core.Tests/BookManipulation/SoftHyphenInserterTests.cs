using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="SoftHyphenInserter"/> (adding soft hyphens).
/// </summary>
public sealed class SoftHyphenInserterTests
{
    private static string Wrap(string content) => "<html><body><p>" + content + "</p></body></html>";

    [Fact]
    public void InsertSoftHyphens_leaves_short_words_untouched()
    {
        string html = Wrap("krótkie słowa tu");

        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        result.Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_inserts_entity_into_a_long_word()
    {
        string html = Wrap("niesamowicie");

        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        result.Should().Contain(SoftHyphenInserter.SoftHyphenEntity);
        result.Replace(SoftHyphenInserter.SoftHyphenEntity, string.Empty).Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_uses_a_numeric_reference_so_the_file_stays_well_formed()
    {
        string html = Wrap("niesamowicie");

        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        SoftHyphenInserter.SoftHyphenEntity.Should().Be("&#173;");
        result.Should().NotContain("&shy;");
        WellFormedChecker.IsWellFormed(result).Should().BeTrue();
    }

    [Theory]
    [InlineData("nie&shy;samowicie")]
    [InlineData("nie&#xAD;samowicie")]
    [InlineData("nie­samowicie")]
    public void InsertSoftHyphens_does_not_split_a_word_that_already_has_a_soft_hyphen_in_another_form(string word)
    {
        string html = Wrap(word);

        SoftHyphenInserter.InsertSoftHyphens(html).Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_is_idempotent()
    {
        string html = Wrap("niesamowicie");

        string once = SoftHyphenInserter.InsertSoftHyphens(html);
        string twice = SoftHyphenInserter.InsertSoftHyphens(once);

        twice.Should().Be(once);
    }

    [Theory]
    [InlineData("pre")]
    [InlineData("code")]
    [InlineData("script")]
    [InlineData("style")]
    public void InsertSoftHyphens_skips_excluded_elements(string tagName)
    {
        string html = $"<html><body><{tagName}>niesamowicie</{tagName}></body></html>";

        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        result.Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_does_not_touch_attribute_values()
    {
        string html = "<html><body><p title=\"niesamowicie\">x</p></body></html>";

        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        result.Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_keeps_minimum_segment_length_around_each_break()
    {
        string html = Wrap("niesamowicie");

        string result = SoftHyphenInserter.InsertSoftHyphens(html);
        int start = result.IndexOf("<p>", System.StringComparison.Ordinal) + "<p>".Length;
        int end = result.IndexOf("</p>", System.StringComparison.Ordinal);
        string word = result[start..end];

        string[] segments = word.Split(SoftHyphenInserter.SoftHyphenEntity);
        segments.Should().OnlyContain(segment => segment.Length >= 3);
    }

    [Fact]
    public void InsertSoftHyphens_does_not_add_more_breaks_to_an_already_hyphenated_long_word()
    {
        string html = Wrap("niesamowicienadzwyczajniewielkolepszy");

        string once = SoftHyphenInserter.InsertSoftHyphens(html);
        string twice = SoftHyphenInserter.InsertSoftHyphens(once);

        twice.Should().Be(once);
    }

    [Fact]
    public void RemoveSoftHyphens_strips_the_entity()
    {
        string html = Wrap("nie" + SoftHyphenInserter.SoftHyphenEntity + "samowicie");

        string result = SoftHyphenInserter.RemoveSoftHyphens(html);

        result.Should().Be(Wrap("niesamowicie"));
    }

    [Fact]
    public void RemoveSoftHyphens_strips_numeric_entity_variants()
    {
        string html = Wrap("nie&#173;samo&#xAD;wicie");

        string result = SoftHyphenInserter.RemoveSoftHyphens(html);

        result.Should().Be(Wrap("niesamowicie"));
    }

    [Fact]
    public void RemoveSoftHyphens_strips_literal_soft_hyphen_character()
    {
        string html = Wrap("nie" + SoftHyphenInserter.SoftHyphenChar + "samowicie");

        string result = SoftHyphenInserter.RemoveSoftHyphens(html);

        result.Should().Be(Wrap("niesamowicie"));
    }

    [Fact]
    public void RemoveSoftHyphens_is_a_noop_when_nothing_to_remove()
    {
        string html = Wrap("krótkie słowa tu");

        string result = SoftHyphenInserter.RemoveSoftHyphens(html);

        result.Should().Be(html);
    }

    [Fact]
    public void RemoveSoftHyphens_reverses_InsertSoftHyphens()
    {
        string html = Wrap("niesamowicienadzwyczajniewielkolepszy krótkie słowa");

        string inserted = SoftHyphenInserter.InsertSoftHyphens(html);
        string removed = SoftHyphenInserter.RemoveSoftHyphens(inserted);

        removed.Should().Be(html);
    }
}
