using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Xml;
using Xunit;

namespace Signet.Benchmarks;

/// <summary>Runs <see cref="SchemaLoadBenchmarks"/> alone: a cold start measured beside other work means nothing.</summary>
[CollectionDefinition(nameof(SchemaLoadBenchmarks), DisableParallelization = true)]
public sealed class SchemaLoadRunsAlone;

/// <summary>
/// What reading EPUBCheck's schemas costs at start-up (<see cref="XmlValidators"/>): each schema the first time this
/// process asks for it (the cost a check pays once, with the parsers still being compiled by the JIT for the first
/// ones), then again from scratch once everything is warm (what a schema costs by itself). As in
/// <see cref="FootnoteBenchmarks"/>, nothing fails over a number: the table is printed and <c>BASELINE-EPUBCHECK.md</c>
/// keeps what was measured before.
/// </summary>
/// <remarks>
/// Run with: <c>SIGNET_BENCHMARKS=1 dotnet test tests/Signet.Benchmarks -c Release --filter SchemaLoadBenchmarks</c>.
/// </remarks>
[Collection(nameof(SchemaLoadBenchmarks))]
public sealed class SchemaLoadBenchmarks
{
    private const int WarmRuns = 3;

    [Fact]
    public void Measure()
    {
        if (Environment.GetEnvironmentVariable("SIGNET_BENCHMARKS") is null)
        {
            Assert.Skip("Set SIGNET_BENCHMARKS=1 to run the benchmarks.");
        }

        StringBuilder report = new("### EPUBCheck schemas — first use in the process, then warm (best of 3)\n\n"
            + "| schema | first use, ms | warm, ms |\n|---|---:|---:|\n");
        long totalCold = 0;
        long totalWarm = 0;
        foreach (XmlValidators validator in Enum.GetValues<XmlValidators>())
        {
            XmlValidator? loaded = null;
            long cold = Once(() => loaded = validator.Get());
            loaded!.Schema.Should().NotBeNull("a benchmark of a schema that did not load is worth nothing");

            string name = loaded.SchemaName;
            bool normative = loaded.IsNormative;
            long warm = Best(WarmRuns, () => _ = new XmlValidator(name, normative));
            totalCold += cold;
            totalWarm += warm;
            report.Append(CultureInfo.InvariantCulture, $"| {name} | {cold} | {warm} |\n");
        }

        report.Append(CultureInfo.InvariantCulture, $"| **all {Enum.GetValues<XmlValidators>().Length}** | **{totalCold}** | **{totalWarm}** |\n");
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
