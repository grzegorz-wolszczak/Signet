using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Messages;

public sealed class SeverityTests
{
    [Theory]
    [InlineData(Severity.Suppressed, "SUPPRESSED", 0)]
    [InlineData(Severity.Usage, "USAGE", 1)]
    [InlineData(Severity.Info, "INFO", 2)]
    [InlineData(Severity.Warning, "WARNING", 3)]
    [InlineData(Severity.Error, "ERROR", 4)]
    [InlineData(Severity.Fatal, "FATAL", 5)]
    public void ToText_And_TryParse_FollowUpstream(Severity severity, string text, int level)
    {
        // Act
        string printed = severity.ToText();
        bool parsed = Severities.TryParse(text, out Severity parsedSeverity);

        // Assert
        printed.Should().Be(text);
        parsed.Should().BeTrue();
        parsedSeverity.Should().Be(severity);
        ((int)severity).Should().Be(level);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("Error")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("CRITICAL")]
    public void TryParse_OfAnythingButTheExactUpperCaseName_Fails(string? text)
    {
        // Act
        bool parsed = Severities.TryParse(text, out _);

        // Assert
        parsed.Should().BeFalse();
    }
}
