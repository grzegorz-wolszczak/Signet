using System.Globalization;
using System.IO;
using Signet.EpubCheck.Core;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>The "checking …" step (upstream's <c>ExecutionSteps</c>).</summary>
internal static class ExecutionSteps
{
    public static void Register(StepRegistry steps) =>
        steps.Add((w, a) => Check(w, a.Get<string>(0)), "checking EPUB/file/document/directory/file\\ set {string}");

    private static void Check(ScenarioWorld world, string path)
    {
        CultureInfo oldCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = world.DefaultCulture;
            world.Mode ??= FromExtension(path);

            string full = world.ResolvePath(path);
            if (!File.Exists(full) && !Directory.Exists(full))
            {
                throw new FileNotFoundException("Couldn’t find EPUB: " + path, full);
            }

            if (world.Report.Format != "default")
            {
                CheckWithReportWriter(world, full);
                return;
            }

            ScenarioReport report = world.Report;
            report.EpubFileName = full;
            report.Initialize();
            world.Checker.Check(new CheckRequest(full, world.Mode.Value, world.Version, world.Profile, report));
            report.Generate();
        }
        finally
        {
            CultureInfo.CurrentUICulture = oldCulture;
        }
    }

    // The checker reports to the writer of the format (XML, XMP or JSON); the steps look at what it wrote.
    private static void CheckWithReportWriter(ScenarioWorld world, string full)
    {
        StringWriter text = new();
        ReportBase writer = world.Report.Format.ToUpperInvariant() switch
        {
            "XML" => new XmlReport(text, full, EpubCheckVersion.Text, closeWriter: false),
            "XMP" => new XmpReport(text, full, EpubCheckVersion.Text, closeWriter: false),
            "JSON" => new CheckingReport(text, full, closeWriter: false),
            _ => throw new ScenarioNotImplementedException($"The {world.Report.Format} report format does not exist."),
        };
        writer.MaxOfEachMessage = world.Report.MaxOfEachMessage;
        writer.Initialize();
        world.Checker.Check(new CheckRequest(full, world.Mode!.Value, world.Version, world.Profile, writer));
        writer.Generate();
        world.ReportText = text.ToString();
    }

    // Upstream's CheckerMode.fromExtension: the kind of document from the extension of the path.
    private static CheckerMode FromExtension(string path)
    {
        int index = path.LastIndexOf('.');
        string extension = index > 0 ? path[(index + 1)..] : "epub";
        return extension switch
        {
            "opf" => CheckerMode.PackageDocument,
            "xhtml" or "html" or "htm" => CheckerMode.XhtmlContentDocument,
            "svg" => CheckerMode.SvgContentDocument,
            "smil" => CheckerMode.MediaOverlaysDocument,
            _ => CheckerMode.Epub,
        };
    }
}
