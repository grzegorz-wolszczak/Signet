using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Galimatias;

/// <summary>The data-driven tests of galimatias' <c>URLTest</c> on its test data (one theory row per line).</summary>
public sealed class GalimatiasDataTests
{
    public static TheoryData<int> WhatwgGood => Lines(TestUrlLoader.Whatwg, good: true);

    public static TheoryData<int> WhatwgBad => Lines(TestUrlLoader.Whatwg, good: false);

    public static TheoryData<int> HostGood => Lines(TestUrlLoader.HostWhatwg, good: true);

    public static TheoryData<int> HostBad => Lines(TestUrlLoader.HostWhatwg, good: false);

    [Theory]
    [MemberData(nameof(WhatwgGood))]
    public void Parse_WhatwgData_GivesTheExpectedUrl(int line)
    {
        // Arrange
        TestUrl test = Find(TestUrlLoader.Whatwg, line);

        // Act
        Url parsed = Url.Parse(test.ParsedBaseUrl, test.RawUrl);

        // Assert
        Url expected = test.ParsedUrl!;
        parsed.Scheme.Should().Be(expected.Scheme, test.ToString());
        parsed.SchemeData.Should().Be(expected.SchemeData, test.ToString());
        parsed.Username.Should().Be(expected.Username, test.ToString());
        parsed.Password.Should().Be(expected.Password, test.ToString());
        parsed.Host.Should().Be(expected.Host, test.ToString());
        parsed.Port.Should().Be(expected.Port, test.ToString());
        parsed.Path.Should().Be(expected.Path, test.ToString());
        parsed.Query.Should().Be(expected.Query, test.ToString());
        parsed.Fragment.Should().Be(expected.Fragment, test.ToString());
        parsed.IsHierarchical.Should().Be(expected.IsHierarchical, test.ToString());
        parsed.Should().Be(expected, test.ToString());
    }

    [Theory]
    [MemberData(nameof(WhatwgBad))]
    public void Parse_WhatwgDataWithoutResult_Fails(int line)
    {
        // Arrange
        TestUrl test = Find(TestUrlLoader.Whatwg, line);

        // Act & Assert
        FluentActions.Invoking(() => Url.Parse(test.ParsedBaseUrl, test.RawUrl)).Should().Throw<GalimatiasParseException>(test.ToString());
    }

    [Theory]
    [MemberData(nameof(HostGood))]
    public void Parse_HostData_GivesTheExpectedHost(int line)
    {
        // Arrange
        TestUrl test = Find(TestUrlLoader.HostWhatwg, line);

        // Act
        Url parsed = Url.Parse(test.ParsedBaseUrl, test.RawUrl);

        // Assert
        parsed.Host.Should().Be(test.ParsedUrl!.Host, test.ToString());
    }

    [Theory]
    [MemberData(nameof(HostBad))]
    public void Parse_HostDataWithoutResult_Fails(int line)
    {
        // Arrange
        TestUrl test = Find(TestUrlLoader.HostWhatwg, line);

        // Act & Assert
        FluentActions.Invoking(() => Url.Parse(test.ParsedBaseUrl, test.RawUrl)).Should().Throw<GalimatiasParseException>(test.ToString());
    }

    [Theory]
    [MemberData(nameof(WhatwgGood))]
    public void WithQueryAndFragment_SetAndRemoveThem(int line)
    {
        // Arrange
        Url url = Find(TestUrlLoader.Whatwg, line).ParsedUrl!;

        // Act
        Url withQuery = url.WithQuery("?query");
        Url withFragment = url.WithFragment("#fragment");
        Url withoutFragment = withFragment.WithFragment(null);

        // Assert
        withQuery.Query.Should().Be("query");
        withQuery.WithQuery(null).Query.Should().BeNull();
        withFragment.Fragment.Should().Be("fragment");
        withoutFragment.Fragment.Should().BeNull();
        withoutFragment.IsHierarchical.Should().Be(url.IsHierarchical); // galimatias made every such URL hierarchical
    }

    [Theory]
    [MemberData(nameof(WhatwgGood))]
    public void WithPath_OnHierarchicalUrl_SetsThePath(int line)
    {
        // Arrange
        Url url = Find(TestUrlLoader.Whatwg, line).ParsedUrl!;

        // Act & Assert
        if (url.IsHierarchical)
        {
            url.WithPath("/foo/bar").Path.Should().Be("/foo/bar");
            url.Authority.Should().Be(url.Host is null ? null : url.Authority);
        }
        else
        {
            url.Authority.Should().BeNull();
            FluentActions.Invoking(() => url.WithPath("/path")).Should().Throw<GalimatiasParseException>();
        }
    }

    private static TestUrl Find(string dataset, int line) => TestUrlLoader.Load(dataset).Single(test => test.Line == line);

    private static TheoryData<int> Lines(string dataset, bool good)
    {
        TheoryData<int> data = [];
        foreach (TestUrl test in TestUrlLoader.Load(dataset).Where(test => (test.ParsedUrl is not null) == good))
        {
            data.Add(test.Line);
        }

        return data;
    }
}
