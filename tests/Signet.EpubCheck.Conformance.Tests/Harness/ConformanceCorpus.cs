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
        "epub-accessibility/accessibility.feature",
        "epub-dictionaries/dictionaries-package-document.feature",
        "epub-dictionaries/dictionaries-publication.feature",
        "epub-distributable-objects/do-package-document.feature",
        "epub-edupub/edupub-content-document-xhtml.feature",
        "epub-edupub/edupub-package-document.feature",
        "epub-edupub/edupub-publication.feature",
        "epub-indexes/indexes-content-document-xhtml.feature",
        "epub-indexes/indexes-package-document.feature",
        "epub-indexes/indexes-publication.feature",
        "epub-multiple-renditions/multiple-rendition-publication.feature",
        "epub-previews/previews-package-document.feature",
        "epub-previews/previews-publication.feature",
        "epub-region-nav/region-nav-publication.feature",
        "epub-scriptable-components/scriptable-components-package-document.feature",
        "epub2/ncx-publication.feature",
        "epub2/ocf-publication.feature",
        "epub2/ops-content-document-svg.feature",
        "epub2/ops-content-document-xhtml.feature",
        "epub2/ops-publication.feature",
        "epub2/opf-package-document.feature",
        "epub2/opf-publication.feature",
        "epub3/00-minimal/minimal.feature",
        "epub3/02-epub-publication-conformance/epub-publication-conformance.feature",
        "epub3/04-ocf/filename-checker.feature",
        "epub3/04-ocf/ocf.feature",
        "epub3/05-package-document/package-document.feature",
        "epub3/06-layouts/layouts.feature",
        "epub3/07-content-documents/content-documents-fixed-layout.feature",
        "epub3/07-content-documents/content-documents-svg.feature",
        "epub3/07-content-documents/content-documents-xhtml.feature",
        "epub3/08-navigation-document/navigation-document.feature",
        "epub3/B-external-identifiers/external-identifiers.feature",
        "epub3/C-viewport-meta-tag/viewport-syntax.feature",
        "epub3/D-vocabularies/media-overlays-vocab.feature",
        "epub3/D-vocabularies/meta-properties-vocab.feature",
        "epub3/D-vocabularies/metadata-link-vocab.feature",
        "epub3/D-vocabularies/package-rendering-vocab.feature",
        "epub3/E-obsolete-features/deprecated-features.feature",
        "epub3/E-obsolete-features/outdated-features.feature",
        "epub3/G-media-type-registrations/media-types-registrations.feature",
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
        AddOcf(pending, "Needs the resource references checker (RSC-007: task 3.6).",
            169, 175, 181, 187, 193, 199, 207, 213, 218, 223, 229, 235, 240, 245, 255, 270);
        AddOcf(pending, "Needs the CSS checker (CSS-008: task 3.7).", 261);
        AddFeature(pending, "epub2/opf-publication.feature", "Needs the resource references checker (RSC-006, RSC-007, RSC-032: task 3.6).", 60, 86, 96, 145);
        AddFeature(pending, "epub2/ncx-publication.feature", "Needs the resource references checker (RSC-007, RSC-010: task 3.6).", 40, 45);
        AddFeature(pending, "epub2/ops-publication.feature", "Needs the resource references checker (RSC-007, RSC-012: task 3.6).", 26, 31);
        AddFeature(pending, "epub3/05-package-document/package-document.feature",
            "Needs the resource references checker (RSC-006, RSC-008, RSC-011, OPF-096: task 3.6).",
            410, 416, 422, 428, 723, 729, 773, 780);
        AddFeature(pending, "epub3/05-package-document/package-document.feature",
            "Needs the CSS checker (the remote fonts and resources of CSS: task 3.7).", 576, 582, 594);
        AddFeature(pending, "epub3/05-package-document/package-document.feature",
            "Needs the media overlay checker (the remote resources of a media overlay: task 3.9).", 612);
        AddFeature(pending, "epub3/07-content-documents/content-documents-svg.feature",
            "Needs the resource references checker (RSC-014, RSC-015: task 3.6).", 199, 206, 213, 220);
        AddFeature(pending, "epub3/07-content-documents/content-documents-xhtml.feature",
            "Needs the resource references checker (RSC-006, RSC-008, RSC-009, RSC-012, RSC-013, RSC-014: task 3.6).",
            225, 230, 278, 283, 288, 308, 314, 356, 362);
        AddFeature(pending, "epub3/07-content-documents/content-documents-xhtml.feature",
            "Needs the CSS checker (CSS-008: task 3.7).", 627, 644);
        AddFeature(pending, "epub3/08-navigation-document/navigation-document.feature",
            "Needs the resource references checker (RSC-010, RSC-011, NAV-011: task 3.6).", 117, 129, 154, 160);
        AddFeature(pending, "epub3/E-obsolete-features/outdated-features.feature",
            "Needs the resource references checker (the outdated content fallback OBS-001, RSC-012: task 3.6).", 18, 26, 35, 43, 165);
        AddFeature(pending, "epub3/E-obsolete-features/outdated-features.feature",
            "Needs the CSS checker (the outdated prefixed properties OBS-001: task 3.7).", 391);
        AddFeature(pending, "epub-dictionaries/dictionaries-publication.feature",
            "Needs the search key map checker and the dictionary profile (OPF-080, RSC-005: task 3.11).", 30, 40);
        AddFeature(pending, "epub-dictionaries/dictionaries-publication.feature",
            "Needs the resource references checker (RSC-007, RSC-021: task 3.6).", 49, 54);
        AddFeature(pending, "epub-multiple-renditions/multiple-rendition-publication.feature",
            "Needs the mapping document of multiple renditions (RSC-005: task 3.11).", 89, 95, 101);
        AddFeature(pending, "epub-region-nav/region-nav-publication.feature",
            "Needs the resource references checker (NAV-009: task 3.6).", 70);
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
