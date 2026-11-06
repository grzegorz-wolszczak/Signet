using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Util.Mime;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Util.Mime;

/// <summary>The cases of upstream's <c>MIMETypeParserTest</c> (EPUBCheck 5.4.0), one row each.</summary>
public sealed class MimeTypeParserTests
{
    [Theory]
    [InlineData("type/subtype", "type/subtype")]
    [InlineData("type/subtype;name=value", "type/subtype;name=value")]
    [InlineData("type/subtype;p1=v1;p2=v2", "type/subtype;p1=v1;p2=v2")]
    [InlineData("type/subtype;name=\"\"", "type/subtype;name=\"\"")]
    [InlineData("type/subtype;name=\"value\"", "type/subtype;name=value")]
    [InlineData("type/subtype;name=\"val ue\"", "type/subtype;name=\"val ue\"")]
    [InlineData("type/subtype;name=\"\\\"\"", "type/subtype;name=\"\\\"\"")]
    [InlineData("type/subtype;name=\"\\\\\"", "type/subtype;name=\"\\\\\"")]
    [InlineData("type/subtype;p1=\"v 1\";p2=v2", "type/subtype;p1=\"v 1\";p2=v2")]
    [InlineData("type/subtype;p1=v1;p2=\"v2\"", "type/subtype;p1=v1;p2=v2")]
    [InlineData("type/subtype;p=v1;p=v2", "type/subtype;p=v1")]
    [InlineData("  type/subtype  ", "type/subtype")]
    [InlineData(" type/subtype", "type/subtype")]
    [InlineData(" type/subtype  ;  p1=v1  ", "type/subtype;p1=v1")]
    [InlineData(" type/subtype  ;  p1=v1  ;  p2=v2  ", "type/subtype;p1=v1;p2=v2")]
    [InlineData("Type/SUBTYPE", "type/subtype")]
    [InlineData(" type/subtype;PaRaM=VaLuE", "type/subtype;param=VaLuE")]
    [InlineData(" type/subtype;PaRaM=\"VaLuE\"", "type/subtype;param=VaLuE")]
    public void Parse_ValidMimeType_SerializesAsUpstream(string value, string expected)
    {
        // Arrange
        RecordingHandler handler = new();

        // Act
        MimeType? parsed = new MimeTypeParser(handler).Parse(value);

        // Assert
        parsed!.ToString().Should().Be(expected);
        handler.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, MimeTypeParseError.Null, -1)]
    [InlineData("", MimeTypeParseError.NoType, -1)]
    [InlineData("/", MimeTypeParseError.NoType, 0)]
    [InlineData("/subtype", MimeTypeParseError.NoType, 0)]
    [InlineData("  /subtype", MimeTypeParseError.NoType, 2)]
    [InlineData("type", MimeTypeParseError.NoSubtype, 4)]
    [InlineData("type/", MimeTypeParseError.NoSubtype, 5)]
    [InlineData("type/  ", MimeTypeParseError.IllegalCodePoint, 5)]
    [InlineData("type /sub", MimeTypeParseError.IllegalCodePoint, 4)]
    [InlineData("type/ sub", MimeTypeParseError.IllegalCodePoint, 5)]
    [InlineData("tyépe/sub", MimeTypeParseError.IllegalCodePoint, 2)]
    [InlineData("type/s@b", MimeTypeParseError.IllegalCodePoint, 6)]
    [InlineData("type/s b", MimeTypeParseError.IllegalCodePoint, 7)]
    public void Parse_InvalidMimeType_FailsAtThePosition(string? value, MimeTypeParseError error, int position)
    {
        // Arrange
        RecordingHandler handler = new();

        // Act
        MimeType? parsed = new MimeTypeParser(handler).Parse(value);

        // Assert
        parsed.Should().BeNull();
        handler.Errors[0].Should().Be((error, position));
    }

    [Theory]
    [InlineData("type/sub;", "type/sub", MimeTypeParseError.NoParamName, 9)]
    [InlineData("type/sub;  ", "type/sub", MimeTypeParseError.NoParamName, 11)]
    [InlineData("type/sub;;p=v", "type/sub;p=v", MimeTypeParseError.NoParamName, 9)]
    [InlineData("type/sub;p", "type/sub", MimeTypeParseError.NoParamValue, 10)]
    [InlineData("type/sub;p;p=v", "type/sub;p=v", MimeTypeParseError.NoParamValue, 10)]
    [InlineData("type/sub;p=", "type/sub", MimeTypeParseError.NoParamValue, 11)]
    [InlineData("type/sub;p=;p=v", "type/sub;p=v", MimeTypeParseError.NoParamValue, 11)]
    [InlineData("type/sub;p=\"v", "type/sub;p=v", MimeTypeParseError.EndQuoteMissing, 13)]
    [InlineData("type/sub;p=\" ", "type/sub;p=\" \"", MimeTypeParseError.EndQuoteMissing, 13)]
    [InlineData("type/sub;p=\"", "type/sub;p=\"\"", MimeTypeParseError.EndQuoteMissing, 12)]
    [InlineData("type/sub;p=\"\\", "type/sub;p=\"\\\\\"", MimeTypeParseError.EndQuoteMissing, 13)]
    [InlineData("type/sub;p=v v", "type/sub;p=\"v v\"", MimeTypeParseError.UnexpectedCodePoint, 12)]
    [InlineData("type/sub;p=v@v", "type/sub;p=\"v@v\"", MimeTypeParseError.UnexpectedCodePoint, 12)]
    [InlineData("type/sub;p=v\"v", "type/sub;p=\"v\\\"v\"", MimeTypeParseError.UnexpectedCodePoint, 12)]
    [InlineData("type/sub;p=v\\v", "type/sub;p=\"v\\\\v\"", MimeTypeParseError.UnexpectedCodePoint, 12)]
    [InlineData("type/sub;p=\"v\"x", "type/sub;p=v", MimeTypeParseError.UnexpectedCodePoint, 14)]
    [InlineData("type/sub;p=\"v\" x", "type/sub;p=v", MimeTypeParseError.UnexpectedCodePoint, 15)]
    [InlineData("type/sub;p = v", "type/sub", MimeTypeParseError.UnexpectedCodePoint, 10)]
    [InlineData("type/sub;p= v", "type/sub;p=\" v\"", MimeTypeParseError.UnexpectedCodePoint, 11)]
    [InlineData("type/sub;p ", "type/sub", MimeTypeParseError.UnexpectedCodePoint, 10)]
    [InlineData("type/sub;p ;p=v", "type/sub;p=v", MimeTypeParseError.UnexpectedCodePoint, 10)]
    [InlineData("type/sub;p= ", "type/sub;p=\"\"", MimeTypeParseError.UnexpectedCodePoint, 11)]
    [InlineData("type/sub;p= ;p=v", "type/sub;p=\"\"", MimeTypeParseError.UnexpectedCodePoint, 11)]
    public void Parse_RecoverableError_ReportsItAndRecovers(string value, string expected, MimeTypeParseError error, int position)
    {
        // Arrange
        RecordingHandler handler = new();

        // Act
        MimeType? parsed = new MimeTypeParser(handler).Parse(value);

        // Assert
        parsed!.ToString().Should().Be(expected);
        handler.Errors[0].Should().Be((error, position));
    }

    [Fact]
    public void FilterParameters_KeepsTheNamedOnesInTheirOrder()
    {
        // Arrange
        MimeType mimeType = MimeType.Parse("text/html;a=1;b=2;c=3")!;

        // Act
        MimeType filtered = mimeType.FilterParameters("c", null, "a");

        // Assert
        filtered.ToString().Should().Be("text/html;a=1;c=3");
    }

    [Fact]
    public void Groups_ClassifyTheEssence()
    {
        // Arrange
        MimeType ogg = MimeType.Of("application", "ogg");

        // Act
        bool audioOrVideo = MimeTypeGroups.IsAudioOrVideo(ogg);
        bool image = MimeTypeGroups.IsImage(ogg);

        // Assert
        audioOrVideo.Should().BeTrue();
        image.Should().BeFalse();
    }

    private sealed class RecordingHandler : IMimeTypeErrorHandler
    {
        public List<(MimeTypeParseError Error, int Position)> Errors { get; } = [];

        public void OnFailure(MimeTypeParseError parseError, int position) => Errors.Add((parseError, position));

        public void OnError(MimeTypeParseError parseError, int position) => Errors.Add((parseError, position));
    }
}
