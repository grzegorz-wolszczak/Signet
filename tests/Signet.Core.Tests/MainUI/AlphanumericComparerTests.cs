using System.Linq;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>Tests of <see cref="AlphanumericComparer"/> — "natural" comparison.</summary>
public sealed class AlphanumericComparerTests
{
    [Theory]
    [InlineData("Section2", "Section10", -1)]
    [InlineData("Section10", "Section2", 1)]
    [InlineData("chapter1", "chapter1", 0)]
    [InlineData("chapter9", "chapter10", -1)]
    [InlineData("a", "b", -1)]
    [InlineData("file2a", "file2b", -1)]
    [InlineData("Section2", "section2", -1)] // wielkosc liter znaczaca (ordinal)
    public void Compare_orders_digit_runs_numerically(string left, string right, int expectedSign)
    {
        int result = AlphanumericComparer.Instance.Compare(left, right);

        System.Math.Sign(result).Should().Be(expectedSign);
    }

    [Fact]
    public void Sorting_a_list_places_numbers_in_human_order()
    {
        string[] names = { "p10", "p2", "p1", "p20", "p3" };

        names.OrderBy(x => x, AlphanumericComparer.Instance)
            .Should().Equal("p1", "p2", "p3", "p10", "p20");
    }

    [Fact]
    public void Empty_keys_do_not_throw()
    {
        AlphanumericComparer.Instance.Compare(string.Empty, "x").Should().BePositive();
        AlphanumericComparer.Instance.Compare(string.Empty, string.Empty).Should().Be(0);
    }
}
