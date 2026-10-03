using AwesomeAssertions;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>
/// Smoke test confirming that the build and test pipeline works
/// (xUnit.v3 + AwesomeAssertions) and that <see cref="ApplicationInfo"/> returns sensible values.
/// </summary>
public sealed class ApplicationInfoTests
{
    [Fact]
    public void Name_is_Signet()
    {
        ApplicationInfo.Name.Should().Be("Signet");
    }

    [Fact]
    public void Version_is_not_blank()
    {
        ApplicationInfo.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NameWithVersion_starts_with_name_and_a_space()
    {
        ApplicationInfo.NameWithVersion.Should().StartWith($"{ApplicationInfo.Name} ");
    }
}
