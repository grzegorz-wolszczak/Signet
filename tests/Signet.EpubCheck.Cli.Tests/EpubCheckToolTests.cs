using System.IO;
using AwesomeAssertions;
using Signet.EpubCheck.Core;
using Xunit;

namespace Signet.EpubCheck.Cli.Tests;

public sealed class EpubCheckToolTests
{
    [Fact]
    public void Run_BeforeTheParserExists_FailsWithTheVersionOnTheErrorStream()
    {
        // Arrange
        using StringWriter output = new();
        using StringWriter error = new();

        // Act
        int exitCode = EpubCheckTool.Run([], output, error);

        // Assert
        exitCode.Should().Be(1);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("EPUBCheck v" + EpubCheckVersion.Text);
    }
}
