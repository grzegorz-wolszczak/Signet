using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

public sealed class UnicodeUtilsTests
{
    [Theory]
    [InlineData("münchen.xhtml", "münchen.xhtml")] // composed and decomposed
    [InlineData("content_ß.xhtml", "content_ss.xhtml")] // full folding of SHARP S
    [InlineData("K.xhtml", "k.xhtml")] // KELVIN SIGN
    [InlineData("ﬁle.xhtml", "file.xhtml")] // LATIN SMALL LIGATURE FI
    [InlineData("Σς.xhtml", "σσ.xhtml")] // capital and final sigma
    [InlineData("\U00010400.xhtml", "\U00010428.xhtml")] // DESERET, outside the BMP
    public void CanonicalCaseFold_NamesUpstreamTakesForTheSame_AreEqual(string left, string right)
    {
        // Act
        string foldedLeft = UnicodeUtils.CanonicalCaseFold(left);
        string foldedRight = UnicodeUtils.CanonicalCaseFold(right);

        // Assert
        foldedLeft.Should().Be(foldedRight);
    }

    [Fact]
    public void CanonicalCaseFold_NamesEqualOnlyAfterCompatibilityNormalization_StayApart()
    {
        // Act (SUPERSCRIPT TWO is "2" only for NFKC, which upstream does not apply)
        string superscript = UnicodeUtils.CanonicalCaseFold("².xhtml");
        string digit = UnicodeUtils.CanonicalCaseFold("2.xhtml");

        // Assert
        superscript.Should().NotBe(digit);
    }

    [Fact]
    public void Normalize_DecomposedText_ComposesIt()
    {
        // Act
        string normalized = UnicodeUtils.Normalize("ü");

        // Assert
        normalized.Should().Be("ü");
    }
}
