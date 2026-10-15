using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Signet.App.Infrastructure;

/// <summary>
/// Notices that an earlier run of the application did not end cleanly. Some failures leave nothing in the log: a stack
/// overflow, a fail-fast or a kill ends the process at once, and .NET cannot catch it or write an exception. Every run
/// therefore leaves a marker file next to the log while it is running and removes it on a clean exit; a marker found at
/// the next start (of a process that is gone) means the run ended abnormally, and the start reports it together with the
/// last lines of that run's log — what the user did just before it.
/// </summary>
public static class CrashSentinel
{
    private const string Prefix = "running-";
    private const string Extension = ".marker";

    /// <summary>
    /// Reports the earlier runs that ended abnormally (their markers are removed) and writes the marker of this run.
    /// </summary>
    /// <param name="directory">The directory with the log files; the markers live there too.</param>
    /// <param name="processId">The process id of this run.</param>
    /// <param name="version">The version of the application, for the report.</param>
    /// <param name="now">The start time of this run.</param>
    /// <param name="isRunning">Whether a process is still running (default: look it up); a marker of a running process is another
    /// instance of the application, not a crash.</param>
    /// <returns>One description per abnormal run, empty when there was none.</returns>
    public static IReadOnlyList<string> StartRun(
        string directory, int processId, string version, DateTimeOffset now, Func<int, bool>? isRunning = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        isRunning ??= IsProcessRunning;
        Directory.CreateDirectory(directory);
        List<string> reports = new();
        foreach (string file in Directory.EnumerateFiles(directory, Prefix + "*" + Extension))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(name.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
                || pid == processId
                || isRunning(pid))
            {
                continue;
            }

            reports.Add(Describe(pid, file));
            TryDelete(file);
        }

        File.WriteAllText(
            MarkerPath(directory, processId),
            string.Create(CultureInfo.InvariantCulture, $"version={version}{Environment.NewLine}started={now:O}{Environment.NewLine}"));
        return reports;
    }

    /// <summary>Removes the marker of this run (a clean exit).</summary>
    public static void EndRun(string directory, int processId) => TryDelete(MarkerPath(directory, processId));

    /// <summary>
    /// The last <paramref name="count"/> lines of the newest log file in <paramref name="directory"/> (the earlier run's
    /// last entries when called before this run logs anything); empty when there is none.
    /// </summary>
    public static IReadOnlyList<string> LogTail(string directory, int count)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        string? newest = Directory.EnumerateFiles(directory, "signet-*.log").OrderBy(f => f, StringComparer.Ordinal).LastOrDefault();
        if (newest is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            // The log may still be open in this process (Serilog): share it.
            using FileStream stream = new(newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            Queue<string> last = new();
            while (reader.ReadLine() is { } line)
            {
                last.Enqueue(line);
                if (last.Count > count)
                {
                    last.Dequeue();
                }
            }

            return last.ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static string MarkerPath(string directory, int processId) =>
        Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{Prefix}{processId}{Extension}"));

    private static string Describe(int pid, string markerFile)
    {
        string details;
        try
        {
            details = string.Join(", ", File.ReadAllLines(markerFile));
        }
        catch (IOException)
        {
            details = "no details";
        }

        return string.Create(CultureInfo.InvariantCulture, $"process {pid} ({details})");
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // A marker that cannot be removed is reported again next time: harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
