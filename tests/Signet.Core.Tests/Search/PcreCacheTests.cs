using System.Globalization;
using AwesomeAssertions;
using Signet.Core.Search;
using Xunit;

namespace Signet.Core.Tests.Search;

/// <summary>
/// Tests for <see cref="PcreCache"/> — an LRU cache of compiled expressions.
/// </summary>
public sealed class PcreCacheTests
{
    [Fact]
    public void GetObject_ReturnsSameInstanceForSameKey()
    {
        // Arrange
        var cache = new PcreCache();

        // Act
        Spcre first = cache.GetObject(@"(\d+)");
        Spcre second = cache.GetObject(@"(\d+)");

        // Assert
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void GetObject_CompilesDistinctPatternsIndependently()
    {
        // Arrange
        var cache = new PcreCache();

        // Act & Assert
        cache.GetObject(@"a").Should().NotBeSameAs(cache.GetObject(@"b"));

        // Assert
        cache.Count.Should().Be(2);
    }

    [Fact]
    public void ExceedingCapacity_EvictsLeastRecentlyUsed()
    {
        // Arrange
        var cache = new PcreCache();

        // Act
        Spcre first = cache.GetObject("p0");
        for (int i = 1; i <= PcreCache.Capacity; i++)
        {
            cache.GetObject("p" + i.ToString(CultureInfo.InvariantCulture));
        }

        // Assert
        cache.Count.Should().Be(PcreCache.Capacity);
        // "p0" was evicted — the next lookup creates a new object.
        // Act & Assert
        cache.GetObject("p0").Should().NotBeSameAs(first);
    }

    [Fact]
    public void RecentUse_ProtectsEntryFromEviction()
    {
        // Arrange
        var cache = new PcreCache();

        // Act
        Spcre kept = cache.GetObject("keep");

        for (int i = 0; i < PcreCache.Capacity - 1; i++)
        {
            cache.GetObject("x" + i.ToString(CultureInfo.InvariantCulture));
            cache.GetObject("keep"); // refresh
        }

        cache.GetObject("x999"); // evicts the oldest entry, but not "keep"

        // Act & Assert
        cache.GetObject("keep").Should().BeSameAs(kept);
    }

    [Fact]
    public void Insert_StoresProvidedInstance()
    {
        // Arrange
        var cache = new PcreCache();
        var spcre = new Spcre("z");

        // Act & Assert
        cache.Insert("z", spcre).Should().BeTrue();

        // Act & Assert
        cache.GetObject("z").Should().BeSameAs(spcre);
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        // Act & Assert
        PcreCache.Instance.Should().BeSameAs(PcreCache.Instance);
    }
}
