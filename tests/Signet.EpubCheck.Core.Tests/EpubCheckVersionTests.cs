using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Signet.EpubCheck.Core.Tests;

public sealed class EpubCheckVersionTests
{
    [Fact]
    public void Text_FollowsTheUpstreamReleaseWithATimestamp()
    {
        // Act
        string text = EpubCheckVersion.Text;

        // Assert
        text.Should().MatchRegex("^" + Regex.Escape(EpubCheckVersion.UpstreamRelease) + @"-\d{12}$");
    }

    [Fact]
    public void UpstreamRelease_IsThePortedStableRelease()
    {
        // Act & Assert
        EpubCheckVersion.UpstreamRelease.Should().Be("5.4.0");
    }
}
