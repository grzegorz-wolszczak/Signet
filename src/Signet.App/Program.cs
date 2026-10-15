using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Signet.App.Infrastructure;
using Signet.Core;
using Signet.Core.Misc;

namespace Signet.App;

/// <summary>Entry point of the desktop application.</summary>
internal static class Program
{
    // Initialization before any use of Avalonia APIs and before the SynchronizationContext.
    [STAThread]
    public static int Main(string[] args)
    {
        string logDir = AppLogging.ConfigureSerilog();
        using ILoggerFactory loggerFactory = AppLogging.CreateLoggerFactory();
        ILogger log = loggerFactory.CreateLogger("Signet.App");

        // An error that reaches the top is logged with what the user did just before it (debug log enabled).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.LogCritical(e.ExceptionObject as Exception, "Unhandled exception{RecentEvents}", RecentEventsText());
        TaskScheduler.UnobservedTaskException += (_, e) =>
            log.LogError(e.Exception, "Unobserved task exception{RecentEvents}", RecentEventsText());

        int exitCode = 0;
        int processId = Environment.ProcessId;
        try
        {
            ReportAbnormalEnds(log, logDir, processId);
            log.LogInformation("Start {App}; log directory: {LogDir}", ApplicationInfo.NameWithVersion, logDir);

            using ServiceProvider provider = AppServices.Build(loggerFactory);
            InitializeScratchpad(provider.GetRequiredService<SettingsStore>(), log);

            App.Services = provider;
            exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            log.LogInformation("Exiting with code {ExitCode}", exitCode);
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "Unhandled exception at the Main level{RecentEvents}", RecentEventsText());
            exitCode = 1;
        }
        finally
        {
            // A clean exit: the marker of this run goes (one left behind means the run ended abnormally).
            CrashSentinel.EndRun(logDir, processId);
            Serilog.Log.CloseAndFlush();
        }

        return exitCode;
    }

    // A stack overflow, a fail-fast or a kill ends the process without a word in the log. Every run leaves a marker while
    // it runs; one found here belongs to a run that is gone: say so, with the last lines of its log.
    private static void ReportAbnormalEnds(ILogger log, string logDir, int processId)
    {
        try
        {
            IReadOnlyList<string> tail = CrashSentinel.LogTail(logDir, 15);
            IReadOnlyList<string> abnormal = CrashSentinel.StartRun(logDir, processId, ApplicationInfo.NameWithVersion, DateTimeOffset.Now);
            foreach (string run in abnormal)
            {
                log.LogWarning(
                    "The previous run ended abnormally — {Run} (a crash the runtime cannot log, e.g. a stack overflow, or the process was killed). Its last log lines:{Tail}",
                    run,
                    Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", tail));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not check whether the previous run ended cleanly");
        }
    }

    // The last debug-log events, as an addition to an error message (empty when the debug log is off).
    private static string RecentEventsText()
    {
        IReadOnlyList<string> events = DebugLog.RecentEvents();
        return events.Count == 0
            ? string.Empty
            : Environment.NewLine + "Last user actions:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", events);
    }

    /// <summary>Avalonia configuration — also used by design-time tools (previewer).</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    // Sets the working directory according to the settings and cleans up orphaned scratchpad directories.
    private static void InitializeScratchpad(SettingsStore settings, ILogger log)
    {
        string home = settings.TempFolderHome;
        if (home.Length > 0)
        {
            TempFolder.ScratchpadRoot = Path.Combine(home, ApplicationInfo.Name, "scratch");
            log.LogInformation("Scratchpad working directory: {Root}", TempFolder.ScratchpadRoot);
        }

        try
        {
            int removed = TempFolder.CleanOrphanedScratchpad();
            if (removed > 0)
            {
                log.LogInformation("Removed {Count} orphaned scratchpad directories", removed);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Cleaning up orphaned scratchpad directories failed");
        }
    }
}
