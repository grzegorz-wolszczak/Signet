using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>Where the upstream scenarios are, and which of them the port is expected to pass already.</summary>
internal static class ConformanceCorpus
{
    /// <summary>Set to <c>1</c> to run every scenario (the ones of features that are not enabled fail where the port falls short).</summary>
    public const string RunAllVariable = "SIGNET_CONFORMANCE_ALL";

    /// <summary>
    /// The .feature files whose scenarios must pass. A feature is added when the part of the port it exercises is done
    /// (<c>EPUBCHECK_PLAN.md</c> names the task); none is yet.
    /// </summary>
    public static readonly IReadOnlySet<string> EnabledFeatures = new HashSet<string>(StringComparer.Ordinal)
    {
        "cli/cli.feature",
        "epub2/ncx-publication.feature",
        "epub2/ocf-publication.feature",
        "epub2/opf-package-document.feature",
        "epub2/opf-publication.feature",
        "epub3/04-ocf/filename-checker.feature",
        "epub3/04-ocf/ocf.feature",
        "epub3/05-package-document/package-document.feature",
        "epub3/C-viewport-meta-tag/viewport-syntax.feature",
        "unit-tests/url-fragment.feature",
    };

    /// <summary>
    /// The scenarios of enabled features that the port does not pass yet (or is not meant to), by <see cref="ScenarioDefinition.Id"/>,
    /// with the reason. Each is skipped; the task of the plan that brings what it needs removes it from here.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PendingScenarios = BuildPending();

    /// <summary>The folder with the copy of upstream's test resources (<c>tests/corpus/epubcheck</c>).</summary>
    public static string Root { get; } = FindRoot();

    public static bool RunAll => Environment.GetEnvironmentVariable(RunAllVariable) == "1";

    public static bool IsEnabled(ScenarioDefinition scenario) =>
        RunAll || (EnabledFeatures.Contains(scenario.FeaturePath) && !PendingScenarios.ContainsKey(scenario.Id));

    /// <summary>Whether a scenario that needs something missing is a failure (it is in an enabled feature and not pending).</summary>
    public static bool MustPass(ScenarioDefinition scenario) =>
        EnabledFeatures.Contains(scenario.FeaturePath) && !PendingScenarios.ContainsKey(scenario.Id);

    private static Dictionary<string, string> BuildPending()
    {
        Dictionary<string, string> pending = new(StringComparer.Ordinal);
        void Add(string reason, params int[] lines)
        {
            foreach (int line in lines)
            {
                pending.Add($"cli/cli.feature:{line}", reason);
            }
        }

        // cli/cli.feature: the command line is done (task 0.6); these scenarios check a publication or write a report.
        Add("Needs the EPUB checker of the port (epics 1-3 of EPUBCHECK_PLAN.md).",
            155, 166, 172, 187, 230, 234, 241, 248, 259, 395);
        Add("Needs the EPUB archive built from an expanded EPUB (task 3.12) and the checker.", 205);
        Add("Needs the XML, XMP and JSON report writers (task 4.2).",
            220, 269, 276, 283, 290, 297, 317, 322, 327, 332, 337, 342, 347);
        Add("Not meant to pass: expects French messages, and the port has English and Polish only (a decision of the plan).",
            366, 371, 376);
        AddOcf(pending, "PKG-025 is reported for manifest items in META-INF, which needs the OPF 3 checks (task 3.3).", 18);
        AddOcf(pending, "Needs the single-document checker and OPF parser (tasks 2.1-2.5).",
            77, 82, 95, 108);
        AddOcf(pending, "Needs content-document and resource-reference checks (tasks 3.5 and 3.6).",
            118, 131, 137, 145, 151, 157, 169, 175, 181, 187, 193, 199, 207, 213, 218, 223, 229, 235, 240, 245, 255, 261, 270);
        AddOcf(pending, "RSC-004 comes from PublicationResourceChecker, which runs with the package document checks (tasks 3.2 and 3.3).", 319);
        AddFeature(pending, "epub2/opf-publication.feature", "Needs the resource and reference registries (RSC-007, RSC-008, RSC-032: task 3.6).", 60, 65, 86, 96, 145);
        AddFeature(pending, "epub2/ncx-publication.feature", "Needs the resource references checker (RSC-007, RSC-010: task 3.6).", 40, 45);
        AddFeature(pending, "epub3/05-package-document/package-document.feature",
            "Needs the content document checks (the properties remote-resources, scripted, svg, switch: task 3.5) and the resource and reference checks (RSC-001, RSC-006, RSC-008, RSC-011, OPF-096: task 3.6).",
            410, 416, 422, 428, 487, 500, 570, 576, 582, 588, 594, 600, 606, 612, 626, 632, 638, 651, 657, 663, 672, 723, 729, 773, 780);
        return pending;
    }

    private static void AddFeature(Dictionary<string, string> pending, string feature, string reason, params int[] lines)
    {
        foreach (int line in lines)
        {
            pending.Add($"{feature}:{line}", reason);
        }
    }

    private static void AddOcf(Dictionary<string, string> pending, string reason, params int[] lines)
    {
        foreach (int line in lines)
        {
            pending.Add($"epub3/04-ocf/ocf.feature:{line}", reason);
        }
    }

    private static string FindRoot()
    {
        string corpus = typeof(ConformanceCorpus).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "CorpusRoot").Value!;
        return Path.GetFullPath(Path.Combine(corpus, "epubcheck"));
    }
}
