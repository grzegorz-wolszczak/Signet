using AwesomeAssertions;
using Xunit;

namespace Signet.EpubCheck.Core.Tests;

public sealed class EpubProfilesTests
{
    [Theory]
    [InlineData("default", EpubProfile.Default)]
    [InlineData("dict", EpubProfile.Dict)]
    [InlineData("EDUPUB", EpubProfile.Edupub)]
    [InlineData("Idx", EpubProfile.Idx)]
    [InlineData("preview", EpubProfile.Preview)]
    public void TryParse_AcceptsTheProfilesInAnyLetterCase(string name, EpubProfile expected)
    {
        // Act
        bool parsed = EpubProfiles.TryParse(name, out EpubProfile profile);

        // Assert
        parsed.Should().BeTrue();
        profile.Should().Be(expected);
        expected.ToText().Should().Be(expected.ToString().ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("epub")]
    [InlineData("1")]
    public void TryParse_OfAnythingElse_Fails(string? name)
    {
        // Act & Assert
        EpubProfiles.TryParse(name, out _).Should().BeFalse();
    }
}
