using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>
/// Tests of <see cref="AsciiFy"/> — transliteration to "close" ASCII. Expected values are
/// derived directly from the <c>unidecode_pos</c> / <c>unidecode_text</c> tables.
/// </summary>
public sealed class AsciiFyTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("plain ascii 123", "plain_ascii_123")]
    [InlineData("keep._-chars", "keep._-chars")]
    public void Ascii_text_only_gets_spaces_replaced(string input, string expected)
    {
        // Act & Assert
        AsciiFy.ConvertToPlainAscii(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("café", "cafe")]
    [InlineData("naïve", "naive")]
    [InlineData("Zażółć gęślą jaźń", "Zazolc_gesla_jazn")]
    [InlineData("Grüße", "Grusse")]
    [InlineData("œuvre", "oeuvre")]
    [InlineData("æther", "aether")]
    [InlineData("Straße", "Strasse")]
    public void Latin_diacritics_are_transliterated(string input, string expected)
    {
        // Act & Assert
        AsciiFy.ConvertToPlainAscii(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Ω", "O")]
    [InlineData("αβπ", "abp")]
    [InlineData("Ярославль", "Aroslavl'")]
    [InlineData("中文", "zhongwen")]
    [InlineData("日本語", "ribenyu")]
    public void Non_latin_scripts_use_the_kbibtex_simplified_table(string input, string expected)
    {
        // Note: this is NOT full unidecode — the KBibTeX table simplifies (e.g. Cyrillic -> single letters).
        // Act & Assert
        AsciiFy.ConvertToPlainAscii(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("a – b", "a_--_b")]    // en-dash -> "--" (replaced before the table lookup)
    [InlineData("a — b", "a_---_b")]   // em-dash -> "---"
    [InlineData("said “hi”", "said_\"hi\"")]
    [InlineData("it’s", "it's")]
    [InlineData("wait…", "wait...")]
    [InlineData("©2026", "(C)2026")]
    public void Punctuation_and_dashes_use_dedicated_replacements(string input, string expected)
    {
        // Act & Assert
        AsciiFy.ConvertToPlainAscii(input).Should().Be(expected);
    }

    [Fact]
    public void Characters_without_a_mapping_are_dropped()
    {
        // Act & Assert
        AsciiFy.ConvertToPlainAscii("€").Should().BeEmpty();

        // Act & Assert
        AsciiFy.ConvertToPlainAscii("a€b").Should().Be("ab");
    }

    [Fact]
    public void Surrogate_pairs_are_passed_through_unchanged()
    {
        // Arrange
        const string emoji = "A\U0001F600B";

        // Act & Assert
        AsciiFy.ConvertToPlainAscii(emoji).Should().Be(emoji);
    }

    [Fact]
    public void ContainsOnlyAscii_true_for_pure_ascii_and_false_for_accents()
    {
        // Act & Assert
        AsciiFy.ContainsOnlyAscii("Hello, world! 42").Should().BeTrue();

        // Act & Assert
        AsciiFy.ContainsOnlyAscii("Zażółć").Should().BeFalse();
    }

    [Fact]
    public void ContainsOnlyAscii_normalizes_to_nfc_first()
    {
        // "e" + U+0301 (combining acute) -> NFC "é" -> no longer ASCII
        // Act & Assert
        AsciiFy.ContainsOnlyAscii("é").Should().BeFalse();
    }

    [Fact]
    public void Data_table_has_the_expected_shape()
    {
        // Act & Assert
        AsciiFyData.Pos.Should().HaveCount(AsciiFyData.PosLength);

        // Act & Assert
        AsciiFyData.Text.Length.Should().BeGreaterThan(20000);
    }
}
