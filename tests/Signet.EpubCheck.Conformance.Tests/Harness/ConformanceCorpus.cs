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
    public static readonly IReadOnlySet<string> EnabledFeatures = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The folder with the copy of upstream's test resources (<c>tests/corpus/epubcheck</c>).</summary>
    public static string Root { get; } = FindRoot();

    public static bool RunAll => Environment.GetEnvironmentVariable(RunAllVariable) == "1";

    public static bool IsEnabled(ScenarioDefinition scenario) => RunAll || EnabledFeatures.Contains(scenario.FeaturePath);

    private static string FindRoot()
    {
        string corpus = typeof(ConformanceCorpus).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "CorpusRoot").Value!;
        return Path.GetFullPath(Path.Combine(corpus, "epubcheck"));
    }
}
