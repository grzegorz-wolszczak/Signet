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
        "epub3/03-resources/resources.feature",
        "epub3/04-ocf/filename-checker.feature",
        "epub3/04-ocf/ocf.feature",
        "epub3/05-package-document/package-document.feature",
        "epub3/05-package-document/vocabularies-association.feature",
        "epub3/06-layouts/layouts.feature",
        "epub3/07-content-documents/content-documents-css.feature",
        "epub3/07-content-documents/content-documents-fixed-layout.feature",
        "epub3/07-content-documents/content-documents-svg.feature",
        "epub3/07-content-documents/content-documents-xhtml.feature",
        "epub3/08-navigation-document/navigation-document.feature",
        "epub3/09-media-overlays/media-overlays.feature",
        "epub3/B-external-identifiers/external-identifiers.feature",
        "epub3/C-viewport-meta-tag/viewport-syntax.feature",
        "epub3/D-vocabularies/media-overlays-vocab.feature",
        "epub3/D-vocabularies/meta-properties-vocab.feature",
        "epub3/D-vocabularies/metadata-link-vocab.feature",
        "epub3/D-vocabularies/package-rendering-vocab.feature",
        "epub3/E-obsolete-features/deprecated-features.feature",
        "epub3/E-obsolete-features/outdated-features.feature",
        "epub3/G-media-type-registrations/media-types-registrations.feature",
        "localization/localization.feature",
        "reporting/json-report.feature",
        "reporting/xml-report.feature",
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
        void Add(string reason, string feature, params int[] lines)
        {
            foreach (int line in lines)
            {
                pending.Add($"{feature}:{line}", reason);
            }
        }

        const string French = "Not meant to pass: expects French messages, and the port has English and Polish only (a decision of the plan).";

        // cli/cli.feature: the command line is done; what is pending here is not meant to pass.
        Add(French, "cli/cli.feature", 366, 371, 376);

        // localization/localization.feature: the scenarios that ask for French (the others - English, Polish by default, the locale
        // reset, case conversion - pass).
        Add(French, "localization/localization.feature", 16, 31, 37);
        return pending;
    }

    private static string FindRoot()
    {
        string corpus = typeof(ConformanceCorpus).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "CorpusRoot").Value!;
        return Path.GetFullPath(Path.Combine(corpus, "epubcheck"));
    }
}
