using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Util;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util;

public sealed class MimeTypeTests
{
    [Fact]
    public void Parse_MixedCaseAndQuotedParameters_NormalizesAndSerializes()
    {
        // Arrange
        const string value = " Type/SUBTYPE;PaRaM=\"value with spaces\";p2=v2";

        // Act
        MimeType? result = MimeType.Parse(value);

        // Assert
        result.Should().NotBeNull();
        result!.Essence.Should().Be("type/subtype");
        result.Parameters.Should().ContainKey("param").WhoseValue.Should().Be("value with spaces");
        result.ToString().Should().Be("type/subtype;param=\"value with spaces\";p2=v2");
    }

    [Fact]
    public void Parse_MissingSubtype_ReportsFailure()
    {
        // Arrange
        RecordingMimeTypeErrorHandler handler = new();

        // Act
        MimeType? result = MimeType.Parse("type/", handler);

        // Assert
        result.Should().BeNull();
        handler.Failures.Should().ContainSingle().Which.Should().Be((MimeTypeParseError.NoSubtype, 5));
    }

    private sealed class RecordingMimeTypeErrorHandler : IMimeTypeErrorHandler
    {
        public List<(MimeTypeParseError Error, int Position)> Failures { get; } = [];

        public void HandleError(MimeTypeParseError parseError, int position)
        {
        }

        public void HandleFailure(MimeTypeParseError parseError, int position) => Failures.Add((parseError, position));
    }
}
