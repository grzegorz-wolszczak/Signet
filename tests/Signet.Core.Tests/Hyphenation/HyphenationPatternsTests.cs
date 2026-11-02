using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.Core.Hyphenation;
using Xunit;

namespace Signet.Core.Tests.Hyphenation;

/// <summary>
/// Tests of <see cref="HyphenationPatterns"/>: reading a libhyphen file and breaking words by its patterns (Liang).
/// The patterns are written for the tests — the files of LibreOffice are not kept in the repository.
/// </summary>
public sealed class HyphenationPatternsTests
{
    // Liang's own example: these patterns break "hyphenation" as "hy-phen-ation".
    private const string LiangExample = "UTF-8\nhy3ph\nhe2n\nhena4\nhen5at\n1na\nn2at\n1tio\n2io\no2n\n";

    private static HyphenationPatterns Parse(string text) => HyphenationPatterns.Parse(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void A_word_breaks_where_the_greatest_point_is_odd()
    {
        // Arrange
        HyphenationPatterns sut = Parse(LiangExample);

        // Act
        var breaks = sut.BreakPoints("hyphenation");

        // Assert
        breaks.Should().Equal(2, 6);
    }

    [Fact]
    public void The_case_of_the_word_does_not_matter()
    {
        // Arrange
        HyphenationPatterns sut = Parse(LiangExample);

        // Act
        var breaks = sut.BreakPoints("Hyphenation");

        // Assert
        breaks.Should().Equal(2, 6);
    }

    [Fact]
    public void The_minimums_of_the_file_keep_letters_at_the_edges()
    {
        // Arrange
        HyphenationPatterns sut = Parse(LiangExample + "LEFTHYPHENMIN 3\nRIGHTHYPHENMIN 6\n");

        // Act
        var breaks = sut.BreakPoints("hyphenation");

        // Assert
        sut.LeftHyphenMin.Should().Be(3);
        sut.RightHyphenMin.Should().Be(6);
        breaks.Should().BeEmpty("the break after \"hy\" leaves two letters, the one after \"hyphen\" five");
    }

    [Fact]
    public void Without_minimums_in_the_file_two_letters_stay_at_each_edge()
    {
        // Arrange
        HyphenationPatterns sut = Parse("UTF-8\n1b\n1c\n1d\n");

        // Act
        var breaks = sut.BreakPoints("abcde");

        // Assert
        sut.LeftHyphenMin.Should().Be(HyphenationPatterns.DefaultHyphenMin);
        breaks.Should().Equal(2, 3);
    }

    [Fact]
    public void A_file_is_read_in_the_encoding_its_first_line_names()
    {
        // Arrange — "ą" is the byte 0xB1 in ISO-8859-2.
        byte[] data = [.. Encoding.ASCII.GetBytes("ISO8859-2\nLEFTHYPHENMIN 1\nRIGHTHYPHENMIN 1\n"), 0xB1, (byte)'1', (byte)'k', (byte)'\n'];

        // Act
        HyphenationPatterns sut = HyphenationPatterns.Parse(data);

        // Assert
        sut.BreakPoints("mąka").Should().Equal(2);
    }

    [Fact]
    public void Comments_and_NOHYPHEN_are_not_patterns()
    {
        // Arrange
        HyphenationPatterns sut = Parse("UTF-8\n% a comment 1a\nNOHYPHEN -,'\nLEFTHYPHENMIN 1\nRIGHTHYPHENMIN 1\nb1c % after a pattern\n");

        // Act
        var breaks = sut.BreakPoints("abcd");

        // Assert
        breaks.Should().Equal(2);
    }

    [Fact]
    public void Non_standard_patterns_are_skipped_and_counted()
    {
        // Arrange
        HyphenationPatterns sut = Parse("UTF-8\nLEFTHYPHENMIN 1\nRIGHTHYPHENMIN 1\nff1a/ff=f,1,2\nb1c\n");

        // Act
        var breaks = sut.BreakPoints("abcffa");

        // Assert
        sut.SkippedPatterns.Should().Be(1);
        breaks.Should().ContainSingle("a soft hyphen cannot change letters, so the non-standard break is not made").Which.Should().Be(2);
    }

    [Fact]
    public void Two_levels_break_a_compound_at_its_boundary_and_its_parts_inside()
    {
        // Arrange — level one finds the boundary "abo|sab", level two breaks each part.
        HyphenationPatterns sut = Parse(
            "UTF-8\nLEFTHYPHENMIN 1\nRIGHTHYPHENMIN 1\nCOMPOUNDLEFTHYPHENMIN 2\nCOMPOUNDRIGHTHYPHENMIN 2\no1s\nNEXTLEVEL\na1b\n");

        // Act
        var breaks = sut.BreakPoints("abosab");

        // Assert
        breaks.Should().Equal(1, 3, 5);
    }

    [Fact]
    public void A_file_without_patterns_is_refused()
    {
        // Act & Assert
        FluentActions.Invoking(() => Parse("UTF-8\nLEFTHYPHENMIN 2\n% nothing else\n"))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void A_word_shorter_than_both_minimums_is_not_broken()
    {
        // Arrange
        HyphenationPatterns sut = Parse(LiangExample);

        // Act
        var breaks = sut.BreakPoints("hyp");

        // Assert
        breaks.Should().BeEmpty();
    }

    [Fact]
    public void Patterns_of_an_installed_file_are_read_from_it()
    {
        // Arrange
        using TestSupport.TempDir dir = new("hyph-");
        string path = dir.Combine("hyph_en_US.dic");
        File.WriteAllText(path, LiangExample);
        HyphenationPatternFile file = new HyphenationPatternFiles(dir.Path).Installed.Single();

        // Act
        HyphenationPatterns sut = HyphenationPatternFiles.Patterns(file);

        // Assert
        sut.BreakPoints("hyphenation").Should().Equal(2, 6);
    }
}
