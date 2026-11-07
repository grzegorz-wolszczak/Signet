using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

// A port of upstream's SourceSetTest, with the density and descriptor values that test does not look at.
public sealed class SourceSetTests
{
    [Theory]
    [InlineData("a", new[] { "a" })]
    [InlineData("abc", new[] { "abc" })]
    [InlineData("a,,b", new[] { "a,,b" })]
    [InlineData("a,", new[] { "a" })]
    [InlineData("a,  ", new[] { "a" })]
    [InlineData("a, b, c", new[] { "a", "b", "c" })]
    [InlineData("a, b 100w, c 200w", new[] { "a", "b", "c" })]
    [InlineData("a, b 1.5x, c 2x", new[] { "a", "b", "c" })]
    [InlineData("a, b 100w 100h, c 200w", new[] { "a", "b", "c" })]
    public void Parse_ValidSourceSet_GivesTheUrlsWithoutErrors(string srcset, string[] urls)
    {
        // Arrange
        List<SourceSetParseError> errors = [];

        // Act
        SourceSet sources = SourceSet.Parse(srcset, (error, _) => errors.Add(error));

        // Assert
        errors.Should().BeEmpty();
        sources.ImageUrls.Should().Equal(urls);
    }

    [Fact]
    public void Parse_DensitiesInVariousSyntaxes_ReportsNoError()
    {
        // Arrange
        List<SourceSetParseError> errors = [];

        // Act
        SourceSet.Parse("u 1x, u 1.5x, u .2x, u 1e12x, u .1e-12x, u 1.1e+12x, u 1E1x", (error, _) => errors.Add(error));

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DescriptorsAreKeptOnTheSources()
    {
        // Act
        SourceSet sources = SourceSet.Parse("a 100w, b 2x, c");

        // Assert
        sources.ImageSources.Should().Equal(new ImageSource("a", 100, null), new ImageSource("b", null, 2f), new ImageSource("c", null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_NullOrEmpty_ReportsItAndIsEmpty(string? srcset)
    {
        // Arrange
        List<SourceSetParseError> errors = [];

        // Act
        SourceSet sources = SourceSet.Parse(srcset, (error, _) => errors.Add(error));

        // Assert
        errors.Should().Contain(SourceSetParseError.NullOrEmpty);
        sources.IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData(",", SourceSetParseError.EmptyStart, new string[0])]
    [InlineData(", image.jpeg", SourceSetParseError.EmptyStart, new[] { "image.jpeg" })]
    [InlineData(",,, a", SourceSetParseError.EmptyStart, new[] { "a" })]
    [InlineData("a,, b", SourceSetParseError.EmptyMiddle, new[] { "a", "b" })]
    [InlineData("a,,,, b", SourceSetParseError.EmptyMiddle, new[] { "a", "b" })]
    [InlineData("a, , b", SourceSetParseError.EmptyMiddle, new[] { "a", "b" })]
    [InlineData("a, ,b", SourceSetParseError.EmptyMiddle, new[] { "a", "b" })]
    public void Parse_EmptyCandidates_AreReportedAndSkipped(string srcset, SourceSetParseError expected, string[] urls)
    {
        // Arrange
        List<SourceSetParseError> errors = [];

        // Act
        SourceSet sources = SourceSet.Parse(srcset, (error, _) => errors.Add(error));

        // Assert
        errors.Should().Contain(expected);
        sources.ImageUrls.Should().Equal(urls);
    }

    [Theory]
    [InlineData("a +100w, b -100w", SourceSetParseError.DescriptorWidthSigned, 2)]
    [InlineData("a 100w 1x", SourceSetParseError.DescriptorMixWidthDensity, 1)]
    [InlineData("a NaNw", SourceSetParseError.DescriptorWidthNotInt, 1)]
    [InlineData("a 0w", SourceSetParseError.DescriptorWidthZero, 1)]
    [InlineData("a 100w 200w", SourceSetParseError.DescriptorWidthMoreThanOnce, 1)]
    [InlineData("a 100w 2z", SourceSetParseError.DescriptorUnknownSuffix, 1)]
    [InlineData("a 1x 100w", SourceSetParseError.DescriptorMixWidthDensity, 1)]
    [InlineData("a NaNx", SourceSetParseError.DescriptorDensityNotFloat, 1)]
    [InlineData("a 1x 2x", SourceSetParseError.DescriptorDensityMoreThanOnce, 1)]
    [InlineData("a 1x 2z", SourceSetParseError.DescriptorUnknownSuffix, 1)]
    [InlineData("a 2z", SourceSetParseError.DescriptorUnknownSuffix, 1)]
    [InlineData("a 0h", SourceSetParseError.DescriptorFutureHZero, 1)]
    [InlineData("a 10h", SourceSetParseError.DescriptorFutureHWithoutWidth, 1)]
    [InlineData("a 10w 0h", SourceSetParseError.DescriptorFutureHZero, 1)]
    [InlineData("a 10w 1h 2h", SourceSetParseError.DescriptorFutureHMoreThanOnce, 1)]
    [InlineData("a 10w xh", SourceSetParseError.DescriptorFutureHNotInt, 1)]
    [InlineData("a 1x 10h", SourceSetParseError.DescriptorFutureHWithDensity, 1)]
    public void Parse_WrongDescriptors_AreReportedAndTheCandidateDropped(string srcset, SourceSetParseError expected, int count)
    {
        // Arrange
        List<SourceSetParseError> errors = [];

        // Act
        SourceSet sources = SourceSet.Parse(srcset, (error, _) => errors.Add(error));

        // Assert
        errors.Should().Equal(System.Linq.Enumerable.Repeat(expected, count));
        sources.ImageUrls.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DescriptorWithParentheses_KeepsTheCommaInside()
    {
        // Arrange
        List<SourceSetParseError> errors = [];

        // Act
        SourceSet sources = SourceSet.Parse("a (1,2) , b", (error, _) => errors.Add(error));

        // Assert
        sources.ImageUrls.Should().Equal("b");
        errors.Should().Equal(SourceSetParseError.DescriptorUnknownSuffix);
    }
}
