using System.Globalization;
using System.IO;

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
                throw new ScenarioNotImplementedException($"The {world.Report.Format} report is not implemented yet (task 4.2).");
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
