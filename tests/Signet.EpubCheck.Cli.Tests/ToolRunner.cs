using System.Globalization;
using System.IO;

namespace Signet.EpubCheck.Cli.Tests;

/// <summary>Runs the tool in the process with its own writers.</summary>
internal static class ToolRunner
{
    internal sealed record Result(int ExitCode, string StdOut, string StdErr);

    internal static Result RunIn(string uiCulture, params string[] args)
    {
        CultureInfo old = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
            using StringWriter output = new() { NewLine = "\n" };
            using StringWriter error = new() { NewLine = "\n" };
            int exitCode = EpubCheckTool.Run(args, output, error);
            return new Result(exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            CultureInfo.CurrentUICulture = old;
        }
    }

    internal static Result Run(params string[] args) => RunIn("en", args);
}
