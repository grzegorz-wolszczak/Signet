using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Signet.EpubCheck.Conformance.Tests.Harness;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Conformance.Tests.Tests;

/// <summary>
/// A differential test against the Java EPUBCheck 5.4.0: the same books are checked by the jar and by the port and the sets of
/// message ids (with the severity) are compared. Local only - it needs a JDK (11 or newer) and the jar - so it is skipped unless
/// <c>SIGNET_JAVA_EPUBCHECK</c> names <c>epubcheck.jar</c>. Like the benchmarks it does not fail over a difference: it writes the
/// differences where <c>SIGNET_DIFFERENTIAL_OUT</c> says and prints them; what is asserted is that the jar ran on every book.
/// </summary>
/// <remarks>
/// <c>SIGNET_JAVA</c> names the java executable (default: <c>java</c> on the path). The books are the expanded ones under
/// <c>tests/corpus</c> (<c>real</c>, <c>broken</c>, <c>epub2</c>, <c>epub3</c>, <c>edge</c>); <c>SIGNET_DIFFERENTIAL_FIXTURES=n</c> adds
/// the first n fixtures of upstream (each run of the jar takes seconds).
/// </remarks>
public sealed class DifferentialTests
{
    private static readonly string[] s_ownFolders = ["real", "broken", "epub2", "epub3", "edge"];

    [Fact]
    public void The_port_reports_what_the_java_tool_reports()
    {
        string? jar = Environment.GetEnvironmentVariable("SIGNET_JAVA_EPUBCHECK");
        if (string.IsNullOrWhiteSpace(jar))
        {
            Assert.Skip("Set SIGNET_JAVA_EPUBCHECK to the path of epubcheck.jar (and SIGNET_JAVA to a JDK 11+ java) to run the differential test.");
        }

        string java = Environment.GetEnvironmentVariable("SIGNET_JAVA") is { Length: > 0 } j ? j : "java";
        string corpus = Path.GetDirectoryName(ConformanceCorpus.Root)!;
        IEnumerable<string> books = s_ownFolders.SelectMany(f => FindBooks(Path.Combine(corpus, f)));
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGNET_DIFFERENTIAL_FIXTURES"), CultureInfo.InvariantCulture, out int fixtures) && fixtures > 0)
        {
            books = books.Concat(FindBooks(ConformanceCorpus.Root).Take(fixtures));
        }

        string work = Path.Combine(Path.GetTempPath(), "differential-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        StringBuilder report = new();
        report.Append("# Differential test against EPUBCheck 5.4.0 (Java)\n\n| book | only in Java | only in the port |\n|---|---|---|\n");
        int checkedBooks = 0;
        int different = 0;
        try
        {
            foreach (string book in books)
            {
                string epub = Path.Combine(work, "book" + checkedBooks + ".epub");
                using (ExpandedEpubArchive.Create(book, keep: true, outputPath: epub))
                {
                }

                HashSet<string> expected = RunJava(java, jar, epub, work);
                HashSet<string> actual = RunPort(epub);
                checkedBooks++;
                string[] missing = expected.Except(actual).Order().ToArray();
                string[] extra = actual.Except(expected).Order().ToArray();
                if (missing.Length + extra.Length > 0)
                {
                    different++;
                    string name = Path.GetRelativePath(corpus, book).Replace('\\', '/');
                    report.Append(CultureInfo.InvariantCulture, $"| `{name}` | {string.Join(", ", missing)} | {string.Join(", ", extra)} |\n");
                }
            }
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }

        report.Append(CultureInfo.InvariantCulture, $"\n{checkedBooks} books, {different} with a difference.\n");
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        if (Environment.GetEnvironmentVariable("SIGNET_DIFFERENTIAL_OUT") is { Length: > 0 } output)
        {
            File.WriteAllText(output, report.ToString(), new UTF8Encoding(false));
        }

        Assert.True(checkedBooks > 0, "no book was found to compare");
    }

    // The expanded books: the folders that hold a mimetype file (not inside another book).
    private static IEnumerable<string> FindBooks(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        Stack<string> pending = new([root]);
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            if (File.Exists(Path.Combine(folder, "mimetype")))
            {
                yield return folder;
                continue;
            }

            foreach (string child in Directory.EnumerateDirectories(folder).OrderByDescending(d => d, StringComparer.Ordinal))
            {
                pending.Push(child);
            }
        }
    }

    // "ID severity" of every message the Java tool reports, usage left out (the port does not report it at its default level).
    private static HashSet<string> RunJava(string java, string jar, string epub, string work)
    {
        string json = Path.Combine(work, "java.json");
        ProcessStartInfo start = new(java) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { "-jar", jar, epub, "--json", json, "--locale", "en" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(File.Exists(json), "the Java tool wrote no report for " + epub);
        JsonNode root = JsonNode.Parse(File.ReadAllText(json))!;
        File.Delete(json);
        return root["messages"]!.AsArray()
            .Select(m => m!["ID"]!.GetValue<string>() + " " + m["severity"]!.GetValue<string>())
            .Where(m => !m.EndsWith("USAGE", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> RunPort(string epub)
    {
        CollectingReport report = new();
        report.SetLocale(CultureInfo.GetCultureInfo("en"));
        report.Initialize();
        OCFChecker.Check(epub, EpubVersion.Unknown, report);
        return report.Messages.Select(m => m.Message.Id.ToText() + " " + m.Message.Severity.ToText()).ToHashSet(StringComparer.Ordinal);
    }
}
