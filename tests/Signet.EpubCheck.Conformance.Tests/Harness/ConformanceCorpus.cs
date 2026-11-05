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
            126, 150, 155, 166, 172, 178, 187, 230, 234, 241, 248, 259, 395);
        Add("Needs the EPUB archive built from an expanded EPUB (task 3.12) and the checker.", 205);
        Add("Needs the XML, XMP and JSON report writers (task 4.2).",
            220, 269, 276, 283, 290, 297, 317, 322, 327, 332, 337, 342, 347);
        Add("Not meant to pass: expects French messages, and the port has English and Polish only (a decision of the plan).",
            366, 371, 376);
        return pending;
    }

    private static string FindRoot()
    {
        string corpus = typeof(ConformanceCorpus).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "CorpusRoot").Value!;
        return Path.GetFullPath(Path.Combine(corpus, "epubcheck"));
    }
}
