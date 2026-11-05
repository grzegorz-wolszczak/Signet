using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

public sealed class ViewportMetaTests
{
    [Fact]
    public void Parse_RepeatedProperties_PreservesAllValues()
    {
        // Arrange
        const string value = "width=device-width, height=800; width=600";

        // Act
        ViewportMeta result = ViewportMeta.Parse(value);

        // Assert
        result.GetValues("width").Should().Equal("device-width", "600");
        result.GetValues("height").Should().ContainSingle().Which.Should().Be("800");
        ViewportMeta.IsValidProperty("width", "device-width").Should().BeTrue();
        ViewportMeta.IsValidProperty("width", "not-a-width").Should().BeFalse();
    }
}
