using System.IO;
using AwesomeAssertions;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>
/// The test process keeps away from the user's files (<see cref="TestIsolation"/>): the temporary folder and the
/// application's data folder are the run's own.
/// </summary>
public sealed class TestIsolationTests
{
    [Fact]
    public void The_temporary_and_the_data_folders_are_the_folders_of_the_run()
    {
        // Act
        string temp = Path.GetTempPath();
        string prefs = AppDirectories.PrefsDirectory;

        // Assert
        TestIsolation.PrefsDirEnvVar.Should().Be(AppDirectories.PrefsDirEnvVar);
        Path.GetFullPath(temp).Should().StartWith(Path.GetFullPath(TestIsolation.TempRoot));
        Path.GetFullPath(prefs).Should().Be(Path.GetFullPath(TestIsolation.PrefsRoot));
    }
}
