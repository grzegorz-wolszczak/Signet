using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>Tests for <see cref="TagAttributes"/> — an ordered attribute map.</summary>
public sealed class TagAttributesTests
{
    [Fact]
    public void Preserves_insertion_order()
    {
        TagAttributes atts = new();
        atts.Set("b", "2");
        atts.Set("a", "1");
        atts.Set("c", "3");

        atts.Keys.Should().Equal("b", "a", "c");
        atts.Pairs().Select(p => p.Value).Should().Equal("2", "1", "3");
    }

    [Fact]
    public void Setting_an_existing_key_updates_value_in_place_without_moving_it()
    {
        TagAttributes atts = new();
        atts.Set("id", "old");
        atts.Set("href", "x");
        atts.Set("id", "new");

        atts.Keys.Should().Equal("id", "href");
        atts.Value("id").Should().Be("new");
    }

    [Fact]
    public void Value_returns_fallback_for_missing_key()
    {
        TagAttributes atts = new();
        atts.Value("missing").Should().BeEmpty();
        atts.Value("missing", "default").Should().Be("default");
        atts.Contains("missing").Should().BeFalse();
    }

    [Fact]
    public void Remove_drops_the_key_and_keeps_the_rest_in_order()
    {
        TagAttributes atts = new();
        atts.Set("a", "1");
        atts.Set("b", "2");
        atts.Set("c", "3");

        atts.Remove("b");

        atts.Keys.Should().Equal("a", "c");
        atts.Count.Should().Be(2);
        atts.Remove("b"); // idempotent
        atts.Count.Should().Be(2);
    }

    [Fact]
    public void Indexer_reads_and_writes()
    {
        TagAttributes atts = new();
        atts["k"] = "v";

        atts["k"].Should().Be("v");
        atts["nope"].Should().BeEmpty();
    }

    [Fact]
    public void Copy_constructor_produces_an_independent_equal_copy()
    {
        TagAttributes original = new();
        original.Set("a", "1");
        original.Set("b", "2");

        TagAttributes copy = new(original);
        copy.Equals(original).Should().BeTrue();

        copy.Set("c", "3");
        copy.Equals(original).Should().BeFalse();
        original.Contains("c").Should().BeFalse();
    }

    [Fact]
    public void Equality_is_order_sensitive()
    {
        TagAttributes first = new();
        first.Set("a", "1");
        first.Set("b", "2");

        TagAttributes reordered = new();
        reordered.Set("b", "2");
        reordered.Set("a", "1");

        first.Equals(reordered).Should().BeFalse();
    }
}
