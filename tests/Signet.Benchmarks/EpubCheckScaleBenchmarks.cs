using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.Benchmarks;

/// <summary>Runs <see cref="EpubCheckScaleBenchmarks"/> alone: a timing measured beside other work means nothing.</summary>
[CollectionDefinition(nameof(EpubCheckScaleBenchmarks), DisableParallelization = true)]
public sealed class EpubCheckScaleRunsAlone;

/// <summary>
/// What a run of the EPUBCheck port costs as the book grows: the packaged EPUB (what <c>epubcheck.net</c> does) and the book as Signet
/// holds it (<see cref="EpubCheckValidator"/>, what Check Book does), on generated books of several sizes. As in
/// <see cref="FootnoteBenchmarks"/>, nothing fails over a number: the table is printed and <c>BASELINE-EPUBCHECK.md</c> keeps what
/// was measured before, to compare by eye. What is asserted is only that the checker saw every file of the book.
/// </summary>
/// <remarks>
/// Run with: <c>SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter EpubCheckScaleBenchmarks</c>.
/// </remarks>
[Collection(nameof(EpubCheckScaleBenchmarks))]
public sealed class EpubCheckScaleBenchmarks
{
    private const int Runs = 2;

    // The same shapes as the footnote benchmarks plus smaller books, so the growth shows: "spread" is many small files, "concentrated"
    // keeps the notes of the book in one file.
    public static TheoryData<string, int, int, SyntheticNotes> Shapes() => new()
    {
        { "50 files", 50, 0, SyntheticNotes.None },
        { "200 files", 200, 0, SyntheticNotes.None },
        { "800 files", 800, 0, SyntheticNotes.None },
        { "1600 files", 1600, 0, SyntheticNotes.None },
        { "spread 300, Signet notes", 300, 6, SyntheticNotes.Signet },
        { "spread 600, Signet notes", 600, 6, SyntheticNotes.Signet },
        { "spread, Signet notes", 1200, 6, SyntheticNotes.Signet },
        { "concentrated, Signet notes", 200, 40, SyntheticNotes.SignetInOneFile },
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
        long size = new FileInfo(epub).Length;

        // A first run warms the process up (the schemas are read once and kept), so the table is about the book and not the start-up.
        long packagedFiles = 0;
        long cold = Once(() => packagedFiles = Packaged(epub));
        long packaged = Best(() => { Packaged(epub); });
        (long allocated, int gen0, int gen2) = Allocations(() => { Packaged(epub); });
        string byType = "";
        if (Environment.GetEnvironmentVariable("SIGNET_ALLOC_PROFILE") is not null)
        {
            using AllocationProfile profile = new();
            Packaged(epub);
            byType = "Allocated by type (sampled, one run on the packaged EPUB):" + Environment.NewLine + Environment.NewLine + profile.Table(25);
        }

        long inMemory;
        long import = Once(() =>
        {
            using Book imported = new ImportEpub(epub).GetBook();
        });
        using (Book loaded = new ImportEpub(epub).GetBook())
        {
            inMemory = Best(() => EpubCheckValidator.Validate(loaded, culture: CultureInfo.GetCultureInfo("en")));
        }

        packagedFiles.Should().BeGreaterThanOrEqualTo(files, "the checker saw every content file of the book it was given");

        string report = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            ### {shape} — {files} files, {notesPerFile * files} notes, {size / 1024} KiB packed

            | what | ms |
            |---|---:|
            | EPUBCheck on the packaged EPUB, first run in the process (cold) | {cold} |
            | EPUBCheck on the packaged EPUB (best of {Runs}) | {packaged} |
            | EPUBCheck on the book in memory, `EpubCheckValidator` (best of {Runs}) | {inMemory} |
            | allocated by one run on the packaged EPUB, KiB per file | {allocated / 1024 / Math.Max(1, files)} |
            | collections of one such run, gen0 / gen2 | {gen0} / {gen2} |
            | (for comparison) importing the EPUB into a book | {import} |

            """);
        Publish(report + byType);
    }

    // The number of files the checker reported a size for: it says every file was looked at.
    private static long Packaged(string epub)
    {
        CollectingReport report = new();
        report.SetLocale(CultureInfo.GetCultureInfo("en"));
        report.Initialize();
        OCFChecker.Check(epub, EpubVersion.Unknown, report);
        return report.Features.Count(f => f.Feature == FeatureKind.Sha256);
    }

    private static long Best(Action work)
    {
        long best = long.MaxValue;
        for (int i = 0; i < Runs; i++)
        {
            best = Math.Min(best, Once(work));
        }

        return best;
    }

    // What one run allocates (all threads, precise) and how many collections it causes: unlike a sampled profile this is exact and does
    // not depend on the machine, so a change meant to save allocations can be judged by it.
    private static (long Bytes, int Gen0, int Gen2) Allocations(Action work)
    {
        GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        int gen0 = GC.CollectionCount(0);
        int gen2 = GC.CollectionCount(2);
        work();
        return (GC.GetTotalAllocatedBytes(precise: true) - before, GC.CollectionCount(0) - gen0, GC.CollectionCount(2) - gen2);
    }

    private static long Once(Action work)
    {
        Stopwatch watch = Stopwatch.StartNew();
        work();
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

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
