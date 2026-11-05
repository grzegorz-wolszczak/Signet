using System.IO;
using Signet.EpubCheck.Core;

namespace Signet.EpubCheck.Cli;

/// <summary>
/// The entry point of <c>epubcheck.net</c>, apart from <c>Main</c> so that tests can run it with their own writers.
/// </summary>
/// <remarks>
/// A skeleton: the argument parser (the same options as the Java EPUBCheck 5.4.0) comes with the next task of
/// <c>EPUBCHECK_PLAN.md</c>.
/// </remarks>
internal static class EpubCheckTool
{
    /// <summary>Runs the tool; returns the process exit code.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Where the tool writes its normal output.</param>
    /// <param name="error">Where the tool writes its errors.</param>
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        _ = args;
        _ = output;
        error.WriteLine($"EPUBCheck v{EpubCheckVersion.Text}: not implemented yet.");
        return 1;
    }
}
