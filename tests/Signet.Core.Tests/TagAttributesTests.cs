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
        // Arrange
        TagAttributes atts = new();

        // Act
        atts.Set("b", "2");
        atts.Set("a", "1");
        atts.Set("c", "3");

        // Assert
        atts.Keys.Should().Equal("b", "a", "c");
        atts.Pairs().Select(p => p.Value).Should().Equal("2", "1", "3");
    }

    [Fact]
    public void Setting_an_existing_key_updates_value_in_place_without_moving_it()
    {
        // Arrange
        TagAttributes atts = new();
        atts.Set("id", "old");
        atts.Set("href", "x");

        // Act
        atts.Set("id", "new");

        // Assert
        atts.Keys.Should().Equal("id", "href");
        atts.Value("id").Should().Be("new");
    }

    [Fact]
    public void Value_returns_fallback_for_missing_key()
    {
        // Arrange
        TagAttributes atts = new();

        // Act & Assert
        atts.Value("missing").Should().BeEmpty();

        // Act & Assert
        atts.Value("missing", "default").Should().Be("default");

        // Act & Assert
        atts.Contains("missing").Should().BeFalse();
    }

    [Fact]
    public void Remove_drops_the_key_and_keeps_the_rest_in_order()
    {
        // Arrange
        TagAttributes atts = new();
        atts.Set("a", "1");
        atts.Set("b", "2");
        atts.Set("c", "3");

        // Act
        atts.Remove("b");

        // Assert
        atts.Keys.Should().Equal("a", "c");
        atts.Count.Should().Be(2);

        // Act
        atts.Remove("b"); // idempotent

        // Assert
        atts.Count.Should().Be(2);
    }

    [Fact]
    public void Indexer_reads_and_writes()
    {
        // Arrange
        TagAttributes atts = new();

        // Act
        atts["k"] = "v";

        // Assert
        atts["k"].Should().Be("v");
        atts["nope"].Should().BeEmpty();
    }

    [Fact]
    public void Copy_constructor_produces_an_independent_equal_copy()
    {
        // Arrange
        TagAttributes original = new();
        original.Set("a", "1");
        original.Set("b", "2");

        // Act
        TagAttributes copy = new(original);

        // Assert
        copy.Equals(original).Should().BeTrue();

        // Act
        copy.Set("c", "3");

        // Assert
        copy.Equals(original).Should().BeFalse();
        original.Contains("c").Should().BeFalse();
    }

    [Fact]
    public void Equality_is_order_sensitive()
    {
        // Arrange
        TagAttributes first = new();
        first.Set("a", "1");
        first.Set("b", "2");

        TagAttributes reordered = new();
        reordered.Set("b", "2");
        reordered.Set("a", "1");

        // Act & Assert
        first.Equals(reordered).Should().BeFalse();
    }
}
