using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// Application logging configuration: Serilog writing to a file in the data directory
/// (<c>&lt;prefs&gt;/logs/signet-.log</c>, daily rotation, 30 days, a new file after 30 MB) and to the console in Debug
/// builds.
/// Exposed to the rest of the application through <see cref="Microsoft.Extensions.Logging"/>.
/// </summary>
internal static class AppLogging
{
    /// <summary>
    /// Creates and installs the global Serilog logger. Returns the directory with the log files
    /// (file names are daily, e.g. <c>signet-20260903.log</c> — Serilog inserts the date
    /// in place of the dot after the hyphen in the <c>signet-.log</c> template).
    /// </summary>
    public static string ConfigureSerilog()
    {
        string logDir = AppDirectories.EnsureLogsDirectory();
        string logPathTemplate = Path.Combine(logDir, "signet-.log");

        LoggerConfiguration config = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPathTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: TimeSpan.FromDays(30),
                fileSizeLimitBytes: 30L * 1024 * 1024,
                rollOnFileSizeLimit: true,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

#if DEBUG
        config = config.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
#endif

        Log.Logger = config.CreateLogger();
        return logDir;
    }

    /// <summary>Creates an <see cref="ILoggerFactory"/> wired to the configured Serilog.</summary>
    public static ILoggerFactory CreateLoggerFactory() =>
        LoggerFactory.Create(builder => builder.AddSerilog(dispose: false));
}
