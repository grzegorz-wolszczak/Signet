using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Keeps a test process away from the user's files and checks that its tests clean up after themselves. Each test
/// project calls <see cref="Install"/> from a module initializer (before any test code runs) and registers
/// <see cref="TempCleanupGuard"/> as an assembly fixture.
/// <para>
/// The process gets a folder of its own, <c>%TEMP%/Signet.TestRuns/&lt;project&gt;-&lt;guid&gt;</c>, with two parts:
/// <c>temp</c>, which <see cref="Path.GetTempPath"/> returns from then on (<c>TMP</c>, <c>TEMP</c>, <c>TMPDIR</c>),
/// and <c>prefs</c>, the application's data folder (<c>SIGNET_PREFS_DIR</c>): settings, logs,
/// dictionaries — never the user's real ones. <c>temp</c> must be empty when the tests end (every test deletes what
/// it made); <c>prefs</c> is the profile of the run and goes with it.
/// </para>
/// </summary>
public static class TestIsolation
{
    /// <summary>The variable that moves the application's data folder (<c>AppDirectories.PrefsDirEnvVar</c> of Signet.Core).</summary>
    public const string PrefsDirEnvVar = "SIGNET_PREFS_DIR";

    private static string? s_runRoot;

    /// <summary>The folder <see cref="Path.GetTempPath"/> returns in this process.</summary>
    public static string TempRoot { get; private set; } = string.Empty;

    /// <summary>The application's data folder in this process.</summary>
    public static string PrefsRoot { get; private set; } = string.Empty;

    /// <summary>Gives this process its own temporary and data folders (see the class remarks).</summary>
    /// <param name="project">
    /// The test project, as the start of the folder's name when the process has no entry assembly (the test project's
    /// own initializer may run after the one of Signet.Core.Tests, whose types it uses).
    /// </param>
    public static void Install(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        if (s_runRoot is not null)
        {
            return;
        }

        string runs = Path.Combine(Path.GetTempPath(), "Signet.TestRuns");
        DeleteStaleRuns(runs);
        string name = Assembly.GetEntryAssembly()?.GetName().Name ?? project;
        s_runRoot = Path.Combine(runs, name + "-" + Guid.NewGuid().ToString("N"));
        TempRoot = Path.Combine(s_runRoot, "temp");
        PrefsRoot = Path.Combine(s_runRoot, "prefs");
        Directory.CreateDirectory(TempRoot);
        Directory.CreateDirectory(PrefsRoot);

        // Path.GetTempPath reads them on every call: TMP/TEMP on Windows, TMPDIR elsewhere. A trailing separator, as
        // Path.GetTempPath returns it.
        string temp = TempRoot + Path.DirectorySeparatorChar;
        Environment.SetEnvironmentVariable("TMP", temp);
        Environment.SetEnvironmentVariable("TEMP", temp);
        Environment.SetEnvironmentVariable("TMPDIR", temp);
        Environment.SetEnvironmentVariable(PrefsDirEnvVar, PrefsRoot);

        // A process that only discovers the tests runs no fixture: its folder goes when it ends. In a test run the guard
        // has already checked and deleted it by then.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteRun();
    }

    // The folders of runs that ended without deleting theirs (a run killed in the middle): older than a day, so never
    // the folder of a run going on.
    private static void DeleteStaleRuns(string runs)
    {
        if (!Directory.Exists(runs))
        {
            return;
        }

        foreach (string folder in Directory.EnumerateDirectories(runs))
        {
            if (Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddDays(-1))
            {
                DeleteFolder(folder);
            }
        }
    }

    /// <summary>What is left in <see cref="TempRoot"/> (paths relative to it), then deletes the whole folder of the run.</summary>
    internal static IReadOnlyList<string> TakeLeftoversAndDelete()
    {
        if (s_runRoot is null)
        {
            return Array.Empty<string>();
        }

        List<string> leftovers = new();
        if (Directory.Exists(TempRoot))
        {
            CollectLeftovers(TempRoot, leftovers);
        }

        leftovers.Sort(StringComparer.Ordinal);

        DeleteRun();
        return leftovers;
    }

    private static void DeleteRun()
    {
        if (s_runRoot is not null)
        {
            DeleteFolder(s_runRoot);
        }
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still open (e.g. a log being written): the rest is gone; the folder is under %TEMP%.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    // The shared parents of the folders TempDir, TempFolder and the designer preview of the main window make: they stay,
    // empty, after their folders were deleted.
    private static readonly string[] SharedParents = { "Signet.Tests", "Signet", Path.Combine("Signet", "scratch"), "Signet.DesignTime" };

    private static void CollectLeftovers(string folder, List<string> leftovers)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(folder))
        {
            string relative = Path.GetRelativePath(TempRoot, path);
            if (!Directory.Exists(path))
            {
                leftovers.Add(relative);
            }
            else if (SharedParents.Contains(relative, StringComparer.Ordinal))
            {
                CollectLeftovers(path, leftovers);
            }
            else
            {
                leftovers.Add(relative + Path.DirectorySeparatorChar);
            }
        }
    }
}

/// <summary>
/// An assembly fixture that fails the test run when its tests left anything in the temporary folder
/// (<see cref="TestIsolation"/>), and deletes the folder of the run.
/// </summary>
public sealed class TempCleanupGuard : IDisposable
{
    /// <inheritdoc />
    public void Dispose()
    {
        IReadOnlyList<string> leftovers = TestIsolation.TakeLeftoversAndDelete();
        if (leftovers.Count > 0)
        {
            throw new InvalidOperationException(
                $"The tests left {leftovers.Count} entries in the temporary folder; every test deletes what it makes "
                + "(TempDir, using): " + Environment.NewLine + string.Join(Environment.NewLine, leftovers.Take(200)));
        }
    }
}
