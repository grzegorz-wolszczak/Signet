using System;
using System.IO;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Xunit;
using SysPath = System.IO.Path;

namespace Signet.Core.Tests;

/// <summary>
/// Collection for tests that override <see cref="TempFolder.ScratchpadRoot"/> — a static
/// property shared by the whole process. <see cref="CollectionDefinitionAttribute.DisableParallelization"/>
/// ensures that tests in this collection never run in parallel with any other collection.
/// </summary>
/// <remarks>
/// Without it the suite is flaky: while <c>ScratchpadRoot</c> points at a test's <see cref="TempDir"/>,
/// tests running in parallel that create a <c>Book</c> put their working folders there, and the
/// <c>Dispose</c> of that <see cref="TempDir"/> deletes them along with all their contents. Books
/// lose files mid-test and validators return empty results — in different tests on every run
/// (BookValidator, FontIntegrityValidator, PreviewMirror, RoundTrip and others).
/// </remarks>
[CollectionDefinition(TempFolderTests.CollectionName, DisableParallelization = true)]
public sealed class ScratchpadRootTestGroup;

/// <summary>
/// Tests for <see cref="TempFolder"/> - the working folder of an unpacked EPUB
/// (RAII create/Dispose plus orphaned-folder cleanup).
/// </summary>
[Collection(CollectionName)]
public sealed class TempFolderTests
{
    /// <summary>Name of the collection that disables parallelization — see <see cref="ScratchpadRootTestGroup"/>.</summary>
    internal const string CollectionName = "ScratchpadRoot (no parallelization)";

    [Fact]
    public void Constructor_creates_a_real_directory()
    {
        using TempDir baseDir = new();
        using TempFolder folder = new(baseDir.Path);

        Directory.Exists(folder.Path).Should().BeTrue();
    }

    [Fact]
    public void Path_has_no_trailing_separator_and_is_absolute()
    {
        using TempDir baseDir = new();
        using TempFolder folder = new(baseDir.Path);

        SysPath.IsPathRooted(folder.Path).Should().BeTrue();
        folder.Path.Should().NotEndWith(SysPath.DirectorySeparatorChar.ToString())
            .And.NotEndWith(SysPath.AltDirectorySeparatorChar.ToString());
    }

    [Fact]
    public void Constructor_with_base_directory_places_folder_directly_under_it()
    {
        using TempDir baseDir = new();
        using TempFolder folder = new(baseDir.Path);

        SysPath.GetDirectoryName(folder.Path).Should().Be(baseDir.Path);
        SysPath.GetFileName(folder.Path).Should().StartWith("Signet-");
    }

    [Fact]
    public void Parameterless_constructor_creates_the_folder_under_the_scratchpad_root()
    {
        using TempFolder folder = new();

        folder.Path.Should().StartWith(TempFolder.ScratchpadRoot);
        Directory.Exists(folder.Path).Should().BeTrue();
    }

    [Fact]
    public void Dispose_removes_the_directory_and_all_of_its_contents()
    {
        using TempDir baseDir = new();
        TempFolder folder = new(baseDir.Path);
        string path = folder.Path;

        Directory.CreateDirectory(SysPath.Combine(path, "EPUB", "Text"));
        File.WriteAllText(SysPath.Combine(path, "mimetype"), "application/epub+zip");
        File.WriteAllText(SysPath.Combine(path, "EPUB", "Text", "ch1.xhtml"), "<html/>");

        folder.Dispose();

        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        using TempDir baseDir = new();
        TempFolder folder = new(baseDir.Path);

        folder.Dispose();
        FluentActions.Invoking(folder.Dispose).Should().NotThrow();
        folder.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Concurrent_instances_are_isolated()
    {
        using TempDir baseDir = new();
        TempFolder first = new(baseDir.Path);
        using TempFolder second = new(baseDir.Path);

        first.Path.Should().NotBe(second.Path);
        Directory.Exists(first.Path).Should().BeTrue();
        Directory.Exists(second.Path).Should().BeTrue();

        first.Dispose();

        Directory.Exists(first.Path).Should().BeFalse();
        Directory.Exists(second.Path).Should().BeTrue("disposing one instance does not affect the other");
    }

    [Fact]
    public void Cleanup_succeeds_even_when_the_folder_was_left_with_a_subtree_after_a_failure()
    {
        using TempDir baseDir = new();
        string path = string.Empty;

        // simulation: an exception while unpacking - the folder is left with partial contents
        try
        {
            using TempFolder folder = new(baseDir.Path);
            path = folder.Path;
            Directory.CreateDirectory(SysPath.Combine(path, "META-INF"));
            File.WriteAllText(SysPath.Combine(path, "META-INF", "container.xml"), "<container/>");
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException)
        {
            // expected
        }

        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void ScratchpadRoot_can_be_overridden_and_restored()
    {
        string original = TempFolder.ScratchpadRoot;
        using TempDir baseDir = new();
        string custom = baseDir.Combine("scratch");

        try
        {
            TempFolder.ScratchpadRoot = custom;
            using TempFolder folder = new();

            SysPath.GetDirectoryName(folder.Path).Should().Be(custom);
        }
        finally
        {
            TempFolder.ScratchpadRoot = original;
        }
    }

    [Fact]
    public void GetPathToScratchpad_creates_the_root_and_returns_it()
    {
        string original = TempFolder.ScratchpadRoot;
        using TempDir baseDir = new();
        string custom = baseDir.Combine("nested", "scratch");

        try
        {
            TempFolder.ScratchpadRoot = custom;

            string result = TempFolder.GetPathToScratchpad();

            result.Should().Be(custom);
            Directory.Exists(custom).Should().BeTrue();
        }
        finally
        {
            TempFolder.ScratchpadRoot = original;
        }
    }

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        FluentActions.Invoking(() => new TempFolder(null!)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new TempFolder("   ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => TempFolder.ScratchpadRoot = null!).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => TempFolder.ScratchpadRoot = "").Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => TempFolder.CleanOrphaned(null!)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => TempFolder.CleanOrphaned("  ")).Should().Throw<ArgumentException>();
    }

    // --- cleaning up orphaned folders (lock file + CleanOrphaned) ---

    [Fact]
    public void A_live_instance_holds_a_lock_file_in_its_directory()
    {
        using TempDir baseDir = new();
        using TempFolder folder = new(baseDir.Path);

        File.Exists(SysPath.Combine(folder.Path, TempFolder.LockFileName)).Should().BeTrue();
    }

    [Fact]
    public void CleanOrphaned_removes_a_folder_left_behind_without_a_live_owner()
    {
        using TempDir baseDir = new();
        string orphan = SysPath.Combine(baseDir.Path, "Signet-01ABCDEF01ABCDEF01ABCDEF01");
        Directory.CreateDirectory(SysPath.Combine(orphan, "EPUB"));
        File.WriteAllText(SysPath.Combine(orphan, TempFolder.LockFileName), string.Empty);
        File.WriteAllText(SysPath.Combine(orphan, "mimetype"), "application/epub+zip");

        int removed = TempFolder.CleanOrphaned(baseDir.Path);

        removed.Should().Be(1);
        Directory.Exists(orphan).Should().BeFalse();
    }

    [Fact]
    public void CleanOrphaned_keeps_a_folder_held_by_a_live_instance()
    {
        using TempDir baseDir = new();
        using TempFolder live = new(baseDir.Path);

        int removed = TempFolder.CleanOrphaned(baseDir.Path);

        removed.Should().Be(0);
        Directory.Exists(live.Path).Should().BeTrue();
    }

    [Fact]
    public void CleanOrphaned_removes_a_lockless_folder_only_once_it_is_old()
    {
        using TempDir baseDir = new();

        string fresh = SysPath.Combine(baseDir.Path, "Signet-fresh0000000000000000000");
        Directory.CreateDirectory(fresh);

        string stale = SysPath.Combine(baseDir.Path, "Signet-stale0000000000000000000");
        Directory.CreateDirectory(stale);
        DateTime past = DateTime.UtcNow.AddHours(-1);
        Directory.SetCreationTimeUtc(stale, past);
        Directory.SetLastWriteTimeUtc(stale, past);

        int removed = TempFolder.CleanOrphaned(baseDir.Path);

        removed.Should().Be(1);
        Directory.Exists(stale).Should().BeFalse("no lock + age > grace period");
        Directory.Exists(fresh).Should().BeTrue("no lock, but fresh — it may be in the middle of being created");
    }

    [Fact]
    public void CleanOrphaned_ignores_directories_without_the_expected_prefix()
    {
        using TempDir baseDir = new();
        string unrelated = SysPath.Combine(baseDir.Path, "some-other-tool-cache");
        Directory.CreateDirectory(unrelated);
        DateTime past = DateTime.UtcNow.AddDays(-30);
        Directory.SetCreationTimeUtc(unrelated, past);
        Directory.SetLastWriteTimeUtc(unrelated, past);

        TempFolder.CleanOrphaned(baseDir.Path).Should().Be(0);
        Directory.Exists(unrelated).Should().BeTrue();
    }

    [Fact]
    public void CleanOrphaned_with_a_mix_removes_only_the_orphan()
    {
        using TempDir baseDir = new();
        using TempFolder live = new(baseDir.Path);

        string orphan = SysPath.Combine(baseDir.Path, "Signet-deadbeefdeadbeefdeadbeef00");
        Directory.CreateDirectory(orphan);
        File.WriteAllText(SysPath.Combine(orphan, TempFolder.LockFileName), string.Empty);

        int removed = TempFolder.CleanOrphaned(baseDir.Path);

        removed.Should().Be(1);
        Directory.Exists(orphan).Should().BeFalse();
        Directory.Exists(live.Path).Should().BeTrue();
    }

    [Fact]
    public void CleanOrphaned_on_a_missing_base_directory_returns_zero()
    {
        string missing = SysPath.Combine(SysPath.GetTempPath(), "Signet.Tests", "does-not-exist-" + Guid.NewGuid().ToString("N"));

        TempFolder.CleanOrphaned(missing).Should().Be(0);
    }

    [Fact]
    public void CleanOrphanedScratchpad_sweeps_the_scratchpad_root()
    {
        string original = TempFolder.ScratchpadRoot;
        using TempDir baseDir = new();
        string custom = baseDir.Combine("scratch");

        try
        {
            TempFolder.ScratchpadRoot = custom;
            Directory.CreateDirectory(custom);
            string orphan = SysPath.Combine(custom, "Signet-0000000000000000000000000A");
            Directory.CreateDirectory(orphan);
            File.WriteAllText(SysPath.Combine(orphan, TempFolder.LockFileName), string.Empty);

            TempFolder.CleanOrphanedScratchpad().Should().Be(1);
            Directory.Exists(orphan).Should().BeFalse();
        }
        finally
        {
            TempFolder.ScratchpadRoot = original;
        }
    }
}
