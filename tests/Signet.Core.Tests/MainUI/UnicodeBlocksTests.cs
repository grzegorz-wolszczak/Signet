using System.Linq;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of the Unicode blocks table — the categories in the "Insert Special Character" dialog.</summary>
public sealed class UnicodeBlocksTests
{
    [Fact]
    public void Blocks_are_non_empty_and_sorted_by_start_codepoint()
    {
        // Act & Assert
        UnicodeBlocks.Blocks.Should().NotBeEmpty();

        // Act & Assert
        UnicodeBlocks.Blocks.Select(b => b.Start).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Blocks_do_not_overlap()
    {
        // Arrange
        UnicodeBlock[] ordered = UnicodeBlocks.Blocks.OrderBy(b => b.Start).ToArray();

        // Act & Assert
        for (int i = 1; i < ordered.Length; i++)
        {
            ordered[i].Start.Should().BeGreaterThan(ordered[i - 1].End);
        }
    }

    [Fact]
    public void Basic_latin_block_covers_ascii_range()
    {
        // Arrange
        UnicodeBlock basicLatin = UnicodeBlocks.Blocks.Single(b => b.Name == "Basic Latin");

        // Act & Assert
        basicLatin.Start.Should().Be(0x0000);

        // Act & Assert
        basicLatin.End.Should().Be(0x007F);
    }

    [Fact]
    public void GetAssignedCodepoints_returns_letters_for_greek_and_coptic()
    {
        // Arrange
        UnicodeBlock greek = UnicodeBlocks.Blocks.Single(b => b.Name == "Greek and Coptic");

        // Act & Assert
        UnicodeBlocks.GetAssignedCodepoints(greek).Should().Contain(0x03B1); // alpha
    }

    [Fact]
    public void GetAssignedCodepoints_respects_max_results()
    {
        // Arrange
        UnicodeBlock basicLatin = UnicodeBlocks.Blocks.Single(b => b.Name == "Basic Latin");

        // Act & Assert
        UnicodeBlocks.GetAssignedCodepoints(basicLatin, maxResults: 5).Should().HaveCount(5);
    }
}
