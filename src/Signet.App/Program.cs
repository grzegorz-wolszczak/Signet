using System;
using System.IO;
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

        int exitCode = 0;
        try
        {
            log.LogInformation("Start {App}; log directory: {LogDir}", ApplicationInfo.NameWithVersion, logDir);

            using ServiceProvider provider = AppServices.Build(loggerFactory);
            InitializeScratchpad(provider.GetRequiredService<SettingsStore>(), log);

            App.Services = provider;
            exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            log.LogInformation("Exiting with code {ExitCode}", exitCode);
        }
        catch (Exception ex)
        {
            log.LogCritical(ex, "Unhandled exception at the Main level");
            exitCode = 1;
        }
        finally
        {
            Serilog.Log.CloseAndFlush();
        }

        return exitCode;
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
