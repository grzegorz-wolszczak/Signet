using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

// The syntax itself is covered by upstream's viewport-syntax.feature (conformance tests).
public sealed class ViewportMetaTests
{
    [Theory]
    [InlineData("width", "device-width", true)]
    [InlineData("width", "1200", true)]
    [InlineData("width", "12.5", true)]
    [InlineData("width", "1.", false)]
    [InlineData("width", ".5", false)]
    [InlineData("width", "device-height", false)]
    [InlineData("height", "device-height", true)]
    [InlineData("height", "١٢", false)] // Java's \d is ASCII only
    [InlineData("initial-scale", "anything", true)]
    public void IsValidProperty_ChecksWidthAndHeightOnly(string name, string value, bool expected)
    {
        // Act
        bool valid = ViewportMeta.IsValidProperty(name, value);

        // Assert
        valid.Should().Be(expected);
    }

    [Fact]
    public void Parse_ReportsTheErrorPositionInCodePoints()
    {
        // Arrange
        List<(ViewportMetaParseError Error, int Position)> errors = [];

        // Act
        ViewportMeta.Parse("\U0001F600=v=", (error, position) => errors.Add((error, position)));

        // Assert (the emoji is one code point: '=' after "v" is the fourth)
        errors.Should().Equal((ViewportMetaParseError.AssignUnexpected, 4));
    }
}
