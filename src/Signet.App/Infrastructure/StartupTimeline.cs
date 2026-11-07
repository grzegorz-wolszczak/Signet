using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Serilog;

namespace Signet.App.Infrastructure;

/// <summary>
/// Where the time of the start of the application goes: each phase is written to the log once, with the time since the process started
/// ("Startup: main window opened at +812 ms"). A slow start is then a matter of reading a few lines of the log, and a change that is meant
/// to speed it up can be judged by them.
/// </summary>
internal static class StartupTimeline
{
    private static readonly ConcurrentDictionary<string, bool> s_seen = new(StringComparer.Ordinal);
    private static readonly DateTime s_processStart = Process.GetCurrentProcess().StartTime;

    /// <summary>Writes <paramref name="phase"/> to the log with the time since the process started; a phase already written is not written again.</summary>
    public static void Mark(string phase)
    {
        if (s_seen.TryAdd(phase, true))
        {
            Log.ForContext("SourceContext", "Signet.App.Startup")
                .Information("Startup: {Phase} at +{Milliseconds} ms", phase, (long)(DateTime.Now - s_processStart).TotalMilliseconds);
        }
    }
}
