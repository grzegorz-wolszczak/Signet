using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Signet.EpubCheck.Cli;
using Signet.EpubCheck.Core;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>The steps that run the command line and look at what it printed (upstream's <c>CLISteps</c>).</summary>
internal static partial class CliSteps
{
    public static void Register(StepRegistry steps)
    {
        steps.Add((w, _) => w.ErrorRedirectedToOutput = true, "stderr is redirected to stdout");
        steps.Add((w, a) => w.ExitCode.Should().Be(a.Get<int>(0)), "the return code is {int}");
        steps.Add((w, a) => w.ReadStream(a.Get<string>(0)).Should().Contain(a.Text(1)),
            "{outputStream} contains", "{outputStream} contains {string}");
        steps.Add((w, a) => w.ReadStream(a.Get<string>(0)).Should().NotContain(a.Text(1)),
            "{outputStream} does not contain", "{outputStream} does not contain {string}");
        steps.Add((w, a) => w.ReadStream(a.Get<string>(0)).Should().BeEmpty(), "{outputStream} is empty");
        steps.Add((w, a) => w.ReadStream(a.Get<string>(0)).TrimEndLineBreak().Should().Be(Replace(a.Text(1))),
            "{outputStream} is", "{outputStream} is {string}");
        steps.Add((w, a) => w.ReadStream(a.Get<string>(0)).Should().StartWith(a.Text(1)),
            "{outputStream} starts with", "{outputStream} starts with {string}");
        steps.Add((w, a) => File.Exists(w.ResolvePath(a.Get<string>(0))).Should().BeFalse(), "file {string} does not exist");
        steps.Add((w, a) =>
        {
            string file = w.ResolvePath(a.Get<string>(0));
            File.Exists(file).Should().BeTrue();
            w.Deletes.Add(file);
        }, "file {string} was created");
        steps.Add((w, a) => Run(w, a.Get<string>(0)), "running `{}`");
        steps.Add(
            (_, _) => throw new ScenarioNotImplementedException("The step needs the report of the command-line run (task 0.6)."),
            "the report uses {int} maximum number of unique messages",
            "the report uses an unlimited number of unique messages");
    }

    // Upstream: the output "is" compared after a trailing CRLF became LF, with {{VERSION}} filled in.
    private static string TrimEndLineBreak(this string text) => TrailingCrLf().Replace(text, "\n");

    private static string Replace(string template) => template.Replace("{{VERSION}}", EpubCheckVersion.Text, StringComparison.Ordinal);

    private static void Run(ScenarioWorld world, string command)
    {
        // The first word is the program name; a word like {{path}} is a test file.
        string[] args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(arg =>
        {
            Match match = TestFile().Match(arg);
            return match.Success ? world.ResolvePath(match.Groups[1].Value) : arg;
        }).ToArray();

        CultureInfo oldCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = world.DefaultCulture;
            world.ExitCode = EpubCheckTool.Run(args, world.StdOut, world.ErrorRedirectedToOutput ? world.StdOut : world.StdErr);
        }
        finally
        {
            CultureInfo.CurrentUICulture = oldCulture;
        }
    }

    [GeneratedRegex(@"^\{\{([^\s{}]+)\}\}$")]
    private static partial Regex TestFile();

    [GeneratedRegex("\r\n$")]
    private static partial Regex TrailingCrLf();
}
