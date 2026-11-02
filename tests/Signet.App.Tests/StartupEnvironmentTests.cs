using System.Collections;
using AutoFixture;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Xunit;

namespace Signet.App.Tests;

/// <summary>What the log says the process was started with (<see cref="StartupEnvironment"/>).</summary>
public sealed class StartupEnvironmentTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void Lists_the_arguments_and_the_diagnostic_variables_sorted_by_name()
    {
        // Arrange
        string dumpName = _fixture.Create<string>();
        string display = _fixture.Create<string>();
        Hashtable environment = new()
        {
            ["DOTNET_EnableCrashReport"] = "1",
            ["DOTNET_DbgMiniDumpName"] = dumpName,
            ["WEBKIT_DISABLE_DMABUF_RENDERER"] = "1",
            ["DISPLAY"] = display,
        };

        // Act
        string text = StartupEnvironment.Describe(["--debug", "my book.epub"], environment);

        // Assert
        text.Should().Contain("Arguments: --debug \"my book.epub\"");
        text.Should().ContainAll(
            $"DISPLAY={display}",
            $"DOTNET_DbgMiniDumpName={dumpName}",
            "DOTNET_EnableCrashReport=1",
            "WEBKIT_DISABLE_DMABUF_RENDERER=1");
        text.IndexOf("DOTNET_DbgMiniDumpName", System.StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("DOTNET_EnableCrashReport", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Leaves_out_the_other_variables()
    {
        // Arrange
        string secret = _fixture.Create<string>();
        Hashtable environment = new() { ["GITHUB_TOKEN"] = secret, ["HOME"] = _fixture.Create<string>() };

        // Act
        string text = StartupEnvironment.Describe([], environment);

        // Assert
        text.Should().NotContain(secret).And.NotContain("GITHUB_TOKEN").And.NotContain("HOME");
        text.Should().Contain("Arguments: none").And.Contain("Environment: none");
    }
}
