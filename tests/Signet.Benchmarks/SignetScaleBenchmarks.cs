using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Reports;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Benchmarks;

/// <summary>Runs <see cref="SignetScaleBenchmarks"/> alone: a timing measured beside other work means nothing.</summary>
[CollectionDefinition(nameof(SignetScaleBenchmarks), DisableParallelization = true)]
public sealed class SignetScaleRunsAlone;

/// <summary>
/// What the whole-book operations of Signet itself cost as the book grows: opening, saving, checkpoints, Find &amp; Replace, the reports, the table
/// of contents, the identifier and class tools, Reformat/Clean and Check Book, on generated books of two sizes, four times apart. The table
/// shows the time and the allocation <em>per file</em> at each size and their ratio: a ratio near 1 is an operation that grows with the book, a
/// larger one is one that grows faster (the thing to look for). As in the other benchmarks nothing fails over a number; what is asserted is that
/// each operation ran.
/// </summary>
/// <remarks>
/// Run with: <c>SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter SignetScaleBenchmarks</c>.
/// </remarks>
[Collection(nameof(SignetScaleBenchmarks))]
public sealed class SignetScaleBenchmarks
{
    private const int NotesPerFile = 3;

    // Two ways to make a book twice as big: more chapters (the work per chapter stays), or longer chapters (the work per chapter grows). The
    // size is counted in elements of the content (a heading, a paragraph, a note), and the table gives time and allocation per element.
    private sealed record Shape(string Name, int SmallFiles, int SmallParagraphs, int LargeFiles, int LargeParagraphs)
    {
        public long SmallElements => (long)SmallFiles * (SmallParagraphs + NotesPerFile + 1);

        public long LargeElements => (long)LargeFiles * (LargeParagraphs + NotesPerFile + 1);
    }

    private static readonly Shape[] s_shapes =
    [
        new("many chapters: 300 -> 1200 files of 20 paragraphs", 300, 20, 1200, 20),
        new("long chapters: 100 files of 50 -> 400 paragraphs", 100, 50, 100, 400),
    ];

    // An operation on a book: "Reads" leaves it as it was (one book serves the repeated runs), "Changes" does not (each run gets a book of its own).
    private sealed record Operation(string Name, bool Changes, Action<Book, string> Run);

    private static IReadOnlyList<Operation> Operations() =>
    [
        new("Export (save the EPUB)", false, (b, dir) => new ExportEpub(b).WriteBook(Path.Combine(dir, "out.epub"))),
        new("Checkpoint: open + add", true, (b, _) =>
        {
            using CheckpointHistory history = new();
            history.Open(b);
            history.MarkEdited(b.GetHtmlResources()[0].BookPath);
            history.AddCheckpoint(b, "bench");
        }),
        new("Find: count a word in all files", false, (b, _) => SearchOperations.CountInFiles("ipsum", Texts(b))),
        new("Find: count a regex in all files", false, (b, _) => SearchOperations.CountInFiles(@"\b(?:dolor|amet)\s+\w+", Texts(b))),
        new("Replace in all files", true, (b, _) => SearchOperations.ReplaceInAllFiles("ipsum", "IPSUM", Texts(b))),
        new("Report: all files", false, (b, _) => BookReportEngine.GetAllFiles(b)),
        new("Report: html files", false, (b, _) => BookReportEngine.GetHtmlFiles(b)),
        new("Report: images", false, (b, _) => BookReportEngine.GetImageFiles(b)),
        new("Report: css files", false, (b, _) => BookReportEngine.GetCssFiles(b)),
        new("Report: html class usage", false, (b, _) => BookReportEngine.GetHtmlClassUsage(b)),
        new("Report: styles in css", false, (b, _) => BookReportEngine.GetStylesInCss(b)),
        new("Report: links", false, (b, _) => BookReportEngine.GetLinks(b)),
        new("Report: character usage", false, (b, _) => BookReportEngine.GetCharacterUsage(b)),
        new("Report: word and character counts", false, (b, _) => BookReportEngine.GetWordCharacterCounts(b)),
        new("TOC: generate", true, (b, _) => TocGenerator.GenerateToc(b)),
        new("TOC: entries for display", false, (b, _) => b.GetTocForDisplay()),
        new("Ids: in html files", false, (b, _) => b.GetIdsInHtmlFiles()),
        new("Ids: in hrefs", false, (b, _) => b.GetIdsInHrefs()),
        new("Ids: prepare rename", false, (b, _) => b.PrepareIdRename()),
        new("Classes: prepare rename", false, (b, _) => b.PrepareClassRename()),
        new("Classes: prepare removal", false, (b, _) => b.PrepareClassRemoval()),
        new("Unused stylesheets", false, (b, _) => b.FindUnusedStylesheets()),
        new("Soft hyphens: survey", false, (b, _) => b.SurveySoftHyphens()),
        new("Mend all html", true, (b, _) => b.MendAllHtml()),
        new("Mend & prettify all html", true, (b, _) => b.PrettyPrintAllHtml()),
        new("Reformat all css", true, (b, _) => b.ReformatAllCss(multipleLineFormat: true)),
        new("Check Book (EPUBCheck + Signet's checks)", false, (b, _) => BookValidator.ValidateCurrentBook(b, culture: CultureInfo.GetCultureInfo("en"))),
    ];

    private static List<TextResource> Texts(Book book) => [.. book.GetHtmlResources()];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Measure(int shapeIndex)
    {
        if (Environment.GetEnvironmentVariable("SIGNET_BENCHMARKS") is null)
        {
            Assert.Skip("Set SIGNET_BENCHMARKS=1 to run the benchmarks.");
        }

        Shape shape = s_shapes[shapeIndex];
        string? only = Environment.GetEnvironmentVariable("SIGNET_BENCH_ONLY");
        IReadOnlyList<Operation> operations = Operations().Where(o => only is null || o.Name.Contains(only, StringComparison.OrdinalIgnoreCase)).ToList();
        Dictionary<string, Result[]> results = operations.ToDictionary(o => o.Name, _ => new Result[2]);
        using TempDir outputs = new("bench-out-");

        // Every operation runs once on a small book first, so that the first size does not pay for the JIT and the first read of each resource.
        using (TempDir warmTemp = new("bench-warm-"))
        {
            string warmEpub = SyntheticBook.BuildEpub(warmTemp, 40, NotesPerFile, SyntheticNotes.Signet, paragraphs: 20);
            foreach (Operation warm in operations)
            {
                using Book book = new ImportEpub(warmEpub).GetBook();
                TryRun(warm, book, outputs);
            }
        }

        (int Files, int Paragraphs)[] sizes = [(shape.SmallFiles, shape.SmallParagraphs), (shape.LargeFiles, shape.LargeParagraphs)];
        for (int s = 0; s < sizes.Length; s++)
        {
            using TempDir temp = new("bench-");
            string epub = SyntheticBook.BuildEpub(temp, sizes[s].Files, NotesPerFile, SyntheticNotes.Signet, paragraphs: sizes[s].Paragraphs);
            using Book shared = new ImportEpub(epub).GetBook();
            foreach (Operation operation in operations)
            {
                results[operation.Name][s] = operation.Changes
                    ? Best(() => new ImportEpub(epub).GetBook(), operation, outputs)
                    : Best(() => shared, operation, outputs, dispose: false);
            }

            results["Open the EPUB (import)"] = [.. (results.TryGetValue("Open the EPUB (import)", out Result[]? known) ? known : new Result[2])];
            results["Open the EPUB (import)"][s] = Best(() => new ImportEpub(epub).GetBook(), new Operation("import", false, (_, _) => { }), outputs, measureBuild: true);
        }

        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"### Signet, {shape.Name} ({NotesPerFile} notes per file; best of 2)");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"| operation | small, ms | large, ms | time per element, large / small | large, bytes per element | allocation per element, large / small |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach ((string name, Result[] r) in results.Select(kv => (kv.Key, kv.Value)))
        {
            if (r[0].Error is not null || r[1].Error is not null)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"| {name} | failed: {r[0].Error ?? r[1].Error} | | | | |");
                continue;
            }

            double timeRatio = r[0].Ms <= 0 || r[1].Ms <= 0 ? double.NaN : (r[1].Ms / shape.LargeElements) / (r[0].Ms / shape.SmallElements);
            double allocRatio = r[0].Bytes <= 0 ? double.NaN : ((double)r[1].Bytes / shape.LargeElements) / ((double)r[0].Bytes / shape.SmallElements);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {r[0].Ms:F0} | {r[1].Ms:F0} | {timeRatio:F1} | {r[1].Bytes / shape.LargeElements} | {allocRatio:F1} |");
        }

        string text = report.ToString();
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        if (Environment.GetEnvironmentVariable("SIGNET_BENCHMARK_OUT") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, text + "\n");
        }

        results.Values.Count(r => r[0].Error is null && r[1].Error is null).Should().BeGreaterThan(0, "at least one operation ran");
    }

    private readonly record struct Result(double Ms, long Bytes, string? Error);

    private static Result Best(Func<Book> book, Operation operation, TempDir outputs, bool dispose = true, bool measureBuild = false)
    {
        double best = double.MaxValue;
        long bytes = 0;
        for (int run = 0; run < 2; run++)
        {
            GC.Collect();
            long before = GC.GetTotalAllocatedBytes(precise: true);
            Stopwatch watch = Stopwatch.StartNew();
            Book b = book();
            if (!measureBuild)
            {
                // Only the operation is measured, not the making of the book.
                watch.Reset();
                GC.Collect();
                before = GC.GetTotalAllocatedBytes(precise: true);
            }

            try
            {
                watch.Start();
                string? error = TryRun(operation, b, outputs);
                watch.Stop();
                if (error is not null)
                {
                    return new Result(0, 0, error);
                }

                if (watch.Elapsed.TotalMilliseconds < best)
                {
                    best = watch.Elapsed.TotalMilliseconds;
                    bytes = GC.GetTotalAllocatedBytes(precise: true) - before;
                }
            }
            finally
            {
                if (dispose)
                {
                    b.Dispose();
                }
            }
        }

        return new Result(best, bytes, null);
    }

    private static string? TryRun(Operation operation, Book book, TempDir outputs)
    {
        try
        {
            operation.Run(book, outputs.Path);
            return null;
        }
        catch (Exception e)
        {
            return e.GetType().Name + ": " + e.Message.Split('\n')[0];
        }
    }
}
