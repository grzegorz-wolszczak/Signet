using System;
using System.IO;
using System.Linq;
using AutoFixture;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// <see cref="CrashSentinel"/>: a run leaves a marker while it runs, a clean exit removes it, and the next start reports
/// the markers of runs that are gone (a stack overflow or a kill leaves nothing in the log).
/// </summary>
public sealed class CrashSentinelTests
{
    private readonly Fixture _fixture = new();

    private static int PidOf(string file) => int.Parse(Path.GetFileNameWithoutExtension(file)["running-".Length..], System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void A_run_leaves_a_marker_until_it_ends_cleanly()
    {
        // Arrange
        using TempDir temp = new();
        int pid = _fixture.Create<ushort>() + 1;

        // Act & Assert
        CrashSentinel.StartRun(temp.Path, pid, "Signet 1", DateTimeOffset.Now).Should().BeEmpty("there was no earlier run");

        // Assert
        Directory.GetFiles(temp.Path, "running-*.marker").Select(PidOf).Should().Equal(pid);

        // Act
        CrashSentinel.EndRun(temp.Path, pid);

        // Assert
        Directory.GetFiles(temp.Path, "running-*.marker").Should().BeEmpty("a clean exit removes the marker");
    }

    [Fact]
    public void A_marker_of_a_run_that_is_gone_is_reported_once_with_its_details()
    {
        // Arrange
        using TempDir temp = new();
        int crashed = _fixture.Create<ushort>() + 1000;
        string version = _fixture.Create<string>();
        CrashSentinel.StartRun(temp.Path, crashed, version, DateTimeOffset.Now, _ => true);

        // Act
        System.Collections.Generic.IReadOnlyList<string> reports = CrashSentinel.StartRun(temp.Path, crashed + 1, "Signet 2", DateTimeOffset.Now, _ => false);

        // Assert
        reports.Should().ContainSingle().Which.Should().Contain(crashed.ToString(System.Globalization.CultureInfo.InvariantCulture)).And.Contain(version);

        // Act & Assert
        CrashSentinel.StartRun(temp.Path, crashed + 2, "Signet 3", DateTimeOffset.Now, _ => false)
            .Should().ContainSingle("only the run of the second start is left (the first was reported and removed)")
            .Which.Should().Contain((crashed + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_marker_of_a_process_that_still_runs_is_another_instance_not_a_crash()
    {
        // Arrange
        using TempDir temp = new();
        int other = _fixture.Create<ushort>() + 1000;
        CrashSentinel.StartRun(temp.Path, other, "Signet", DateTimeOffset.Now, _ => true);

        // Act & Assert
        CrashSentinel.StartRun(temp.Path, other + 1, "Signet", DateTimeOffset.Now, pid => pid == other).Should().BeEmpty();

        // Assert
        File.Exists(Path.Combine(temp.Path, $"running-{other}.marker")).Should().BeTrue("the other instance keeps its marker");
    }

    [Fact]
    public void The_log_tail_is_the_last_lines_of_the_newest_log_file()
    {
        // Arrange
        using TempDir temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "signet-20261001.log"), "old");
        string[] lines = Enumerable.Range(0, 10).Select(i => _fixture.Create<string>() + i).ToArray();
        File.WriteAllLines(Path.Combine(temp.Path, "signet-20261002.log"), lines);

        // Act & Assert
        CrashSentinel.LogTail(temp.Path, 3).Should().Equal(lines.TakeLast(3));
        CrashSentinel.LogTail(Path.Combine(temp.Path, "missing"), 3).Should().BeEmpty();
    }
}
