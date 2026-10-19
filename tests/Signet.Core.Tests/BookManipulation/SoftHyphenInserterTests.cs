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
        // Arrange
        string html = Wrap("krótkie słowa tu");

        // Act
        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        // Assert
        result.Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_inserts_entity_into_a_long_word()
    {
        // Arrange
        string html = Wrap("niesamowicie");

        // Act
        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        // Assert
        result.Should().Contain(SoftHyphenInserter.SoftHyphenEntity);
        result.Replace(SoftHyphenInserter.SoftHyphenEntity, string.Empty).Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_uses_a_numeric_reference_so_the_file_stays_well_formed()
    {
        // Arrange
        string html = Wrap("niesamowicie");

        // Act
        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        // Assert
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
        // Arrange
        string html = Wrap(word);

        // Act & Assert
        SoftHyphenInserter.InsertSoftHyphens(html).Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_is_idempotent()
    {
        // Arrange
        string html = Wrap("niesamowicie");

        // Act
        string once = SoftHyphenInserter.InsertSoftHyphens(html);
        string twice = SoftHyphenInserter.InsertSoftHyphens(once);

        // Assert
        twice.Should().Be(once);
    }

    [Theory]
    [InlineData("pre")]
    [InlineData("code")]
    [InlineData("script")]
    [InlineData("style")]
    public void InsertSoftHyphens_skips_excluded_elements(string tagName)
    {
        // Arrange
        string html = $"<html><body><{tagName}>niesamowicie</{tagName}></body></html>";

        // Act
        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        // Assert
        result.Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_does_not_touch_attribute_values()
    {
        // Arrange
        string html = "<html><body><p title=\"niesamowicie\">x</p></body></html>";

        // Act
        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        // Assert
        result.Should().Be(html);
    }

    [Fact]
    public void InsertSoftHyphens_keeps_minimum_segment_length_around_each_break()
    {
        // Arrange
        string html = Wrap("niesamowicie");

        // Act
        string result = SoftHyphenInserter.InsertSoftHyphens(html);

        // Assert
        int start = result.IndexOf("<p>", System.StringComparison.Ordinal) + "<p>".Length;
        int end = result.IndexOf("</p>", System.StringComparison.Ordinal);
        string word = result[start..end];

        string[] segments = word.Split(SoftHyphenInserter.SoftHyphenEntity);
        segments.Should().OnlyContain(segment => segment.Length >= 3);
    }

    [Fact]
    public void InsertSoftHyphens_does_not_add_more_breaks_to_an_already_hyphenated_long_word()
    {
        // Arrange
        string html = Wrap("niesamowicienadzwyczajniewielkolepszy");

        // Act
        string once = SoftHyphenInserter.InsertSoftHyphens(html);
        string twice = SoftHyphenInserter.InsertSoftHyphens(once);

        // Assert
        twice.Should().Be(once);
    }

    [Fact]
    public void RemoveSoftHyphens_strips_the_entity()
    {
        // Arrange
        string html = Wrap("nie" + SoftHyphenInserter.SoftHyphenEntity + "samowicie");

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(html);

        // Assert
        result.Should().Be(Wrap("niesamowicie"));
    }

    [Fact]
    public void RemoveSoftHyphens_strips_numeric_entity_variants()
    {
        // Arrange
        string html = Wrap("nie&#173;samo&#xAD;wicie");

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(html);

        // Assert
        result.Should().Be(Wrap("niesamowicie"));
    }

    [Fact]
    public void RemoveSoftHyphens_strips_literal_soft_hyphen_character()
    {
        // Arrange
        string html = Wrap("nie" + SoftHyphenInserter.SoftHyphenChar + "samowicie");

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(html);

        // Assert
        result.Should().Be(Wrap("niesamowicie"));
    }

    [Fact]
    public void RemoveSoftHyphens_is_a_noop_when_nothing_to_remove()
    {
        // Arrange
        string html = Wrap("krótkie słowa tu");

        // Act
        string result = SoftHyphenInserter.RemoveSoftHyphens(html);

        // Assert
        result.Should().Be(html);
    }

    [Fact]
    public void RemoveSoftHyphens_reverses_InsertSoftHyphens()
    {
        // Arrange
        string html = Wrap("niesamowicienadzwyczajniewielkolepszy krótkie słowa");

        // Act
        string inserted = SoftHyphenInserter.InsertSoftHyphens(html);
        string removed = SoftHyphenInserter.RemoveSoftHyphens(inserted);

        // Assert
        removed.Should().Be(html);
    }
}
