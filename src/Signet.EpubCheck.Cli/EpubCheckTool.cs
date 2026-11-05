using System.Globalization;
using System.IO;

namespace Signet.EpubCheck.Cli;

/// <summary>
/// The entry point of <c>epubcheck.net</c>, apart from <c>Main</c> so that tests can run it with their own writers.
/// </summary>
internal static class EpubCheckTool
{
    /// <summary>Runs the tool; returns the process exit code.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Where the tool writes its normal output.</param>
    /// <param name="error">Where the tool writes its errors.</param>
    internal static int Run(string[] args, TextWriter output, TextWriter error) =>
        new ToolRun(new ToolOutput(output, error), CultureInfo.CurrentUICulture).Run(args);
}
