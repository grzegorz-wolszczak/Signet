using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Benchmarks;

/// <summary>
/// What the footnote operations cost on a book of a real size. These do not run with the other tests: they take
/// minutes, and a timing is far too noisy to assert on — the same code measured 2145 ms and 3156 ms on one machine in
/// one session. So nothing here fails over a number. The run prints a table, writes it where
/// <c>SIGNET_BENCHMARK_OUT</c> says to, and <c>BASELINE.md</c> beside this file keeps what was measured before, to
/// compare by eye. What is asserted is only that the operations did their work, so a benchmark cannot pass by doing
/// nothing.
/// </summary>
/// <remarks>
/// Run with: <c>SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release</c>.
/// </remarks>
public sealed class FootnoteBenchmarks
{
    // Each shape is measured the way its name says: "spread" is a book of many small files (what a real book of
    // 1300 chapters looks like), "concentrated" keeps every note in one notes file (the shape every quadratic found
    // so far needed in order to show itself).
    public static TheoryData<string, int, int, SyntheticNotes> Shapes() => new()
    {
        { "spread, Signet", 1200, 6, SyntheticNotes.Signet },
        { "concentrated, Signet", 200, 40, SyntheticNotes.SignetInOneFile },
        { "spread, foreign", 1200, 6, SyntheticNotes.Foreign },
        { "concentrated, foreign", 200, 40, SyntheticNotes.ForeignInOneFile },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Measure(string shape, int files, int notesPerFile, SyntheticNotes notes)
    {
        if (Environment.GetEnvironmentVariable("SIGNET_BENCHMARKS") is null)
        {
            Assert.Skip("Set SIGNET_BENCHMARKS=1 to run the benchmarks.");
        }

        using TempDir temp = new("bench-");
        string epub = SyntheticBook.BuildEpub(temp, files, notesPerFile, notes);
        bool signet = notes is SyntheticNotes.Signet or SyntheticNotes.SignetInOneFile;
        int expected = files * notesPerFile;
        StringBuilder report = new();
        report.Append(CultureInfo.InvariantCulture,
            $"### {shape} — {files} files x {notesPerFile} notes = {expected}\n\n| operation | ms |\n|---|---:|\n");

        // Reading the book does not change it, so one book serves all of them and the best of three runs is kept:
        // the best is the one least spoiled by whatever else the machine was doing.
        using (Book book = new ImportEpub(epub).GetBook())
        {
            FootnoteScanner.Scan(book).Should().HaveCount(expected, "the generated book holds what was asked for");
            Row(report, "FootnoteScanner.Scan", Best(3, () => FootnoteScanner.Scan(book)));
            Row(report, "FootnoteValidator.Validate", Best(3, () => FootnoteValidator.Validate(book)));
            Row(report, "LinkIntegrityValidator.Validate", Best(3, () => LinkIntegrityValidator.Validate(book)));
            Row(report, "PlanConversion", Best(3, () => FootnoteEditor.PlanConversion(book)));
            Row(report, "PlanPlacement", Best(3, () => FootnoteEditor.PlanPlacement(book)));
        }

        // The rest change the book, so each one gets a book of its own.
        using (Book book = new ImportEpub(epub).GetBook())
        {
            Row(report, "Renumber", Once(() => FootnoteEditor.Renumber(book, null, null, TestContext.Current.CancellationToken)));
        }

        if (!signet)
        {
            using Book book = new ImportEpub(epub).GetBook();
            ConversionPlan plan = FootnoteEditor.PlanConversion(book);
            ConversionResult? result = null;
            Row(report, "Convert", Once(() => result = FootnoteEditor.Convert(
                book,
                new ConversionRequest(plan.Items, plan.AutomaticFile?.File, false),
                null,
                null,
                TestContext.Current.CancellationToken)));

            // A conversion that reported trouble would have been rewound by the caller, so its timing means nothing.
            result!.Problems.Should().BeEmpty("a benchmark of a conversion is only worth reading when it succeeded");
            result.Converted.Should().Be(expected);
        }

        Publish(report.ToString());
    }

    private static long Best(int times, Action work)
    {
        long best = long.MaxValue;
        for (int i = 0; i < times; i++)
        {
            best = Math.Min(best, Once(work));
        }

        return best;
    }

    private static long Once(Action work)
    {
        Stopwatch watch = Stopwatch.StartNew();
        work();
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

    private static void Row(StringBuilder report, string name, long ms) =>
        report.Append(CultureInfo.InvariantCulture, $"| {name} | {ms} |\n");

    private static void Publish(string report)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(report);
        if (Environment.GetEnvironmentVariable("SIGNET_BENCHMARK_OUT") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, report + "\n");
        }
    }
}
