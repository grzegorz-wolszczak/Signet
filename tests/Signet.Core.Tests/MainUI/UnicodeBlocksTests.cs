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
        UnicodeBlocks.Blocks.Should().NotBeEmpty();
        UnicodeBlocks.Blocks.Select(b => b.Start).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Blocks_do_not_overlap()
    {
        UnicodeBlock[] ordered = UnicodeBlocks.Blocks.OrderBy(b => b.Start).ToArray();
        for (int i = 1; i < ordered.Length; i++)
        {
            ordered[i].Start.Should().BeGreaterThan(ordered[i - 1].End);
        }
    }

    [Fact]
    public void Basic_latin_block_covers_ascii_range()
    {
        UnicodeBlock basicLatin = UnicodeBlocks.Blocks.Single(b => b.Name == "Basic Latin");
        basicLatin.Start.Should().Be(0x0000);
        basicLatin.End.Should().Be(0x007F);
    }

    [Fact]
    public void GetAssignedCodepoints_returns_letters_for_greek_and_coptic()
    {
        UnicodeBlock greek = UnicodeBlocks.Blocks.Single(b => b.Name == "Greek and Coptic");

        UnicodeBlocks.GetAssignedCodepoints(greek).Should().Contain(0x03B1); // alpha
    }

    [Fact]
    public void GetAssignedCodepoints_respects_max_results()
    {
        UnicodeBlock basicLatin = UnicodeBlocks.Blocks.Single(b => b.Name == "Basic Latin");

        UnicodeBlocks.GetAssignedCodepoints(basicLatin, maxResults: 5).Should().HaveCount(5);
    }
}
