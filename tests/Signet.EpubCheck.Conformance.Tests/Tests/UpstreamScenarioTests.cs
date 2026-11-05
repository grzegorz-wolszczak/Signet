using System;
using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Conformance.Tests.Harness;
using Xunit;

namespace Signet.EpubCheck.Conformance.Tests.Tests;

/// <summary>
/// One test per scenario of upstream's conformance suite. The scenarios of the features listed in
/// <see cref="ConformanceCorpus.EnabledFeatures"/> must pass; the others are skipped, with the reason, until the part of the
/// port they exercise is done. <c>SIGNET_CONFORMANCE_ALL=1</c> runs all of them (a scenario that needs something the port
/// does not have yet is then skipped, any other failure is shown).
/// </summary>
public sealed class UpstreamScenarioTests
{
    private const string NotEnabled =
        "The port does not pass this feature yet (ConformanceCorpus.EnabledFeatures; SIGNET_CONFORMANCE_ALL=1 runs it anyway).";

    private static readonly Lazy<IReadOnlyList<ScenarioDefinition>> s_scenarios = new(() => FeatureLoader.LoadAll(ConformanceCorpus.Root));

    private static readonly Lazy<StepRegistry> s_steps = new(StepRegistry.CreateDefault);

    public static IEnumerable<ITheoryDataRow> Scenarios() =>
        s_scenarios.Value.Select(s => new TheoryDataRow<string>(s.Id)
        {
            TestDisplayName = s.DisplayName,
            Skip = ConformanceCorpus.IsEnabled(s) ? null
                : ConformanceCorpus.PendingScenarios.TryGetValue(s.Id, out string? reason) ? reason : NotEnabled,
        });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Scenario(string id)
    {
        // Arrange
        ScenarioDefinition scenario = s_scenarios.Value.Single(s => s.Id == id);
        ScenarioWorld world = new(ConformanceCorpus.Root, new UnavailableEpubChecker());

        // Act
        try
        {
            ScenarioRunner.Run(scenario, s_steps.Value, world);
        }
        catch (ScenarioNotImplementedException ex) when (!ConformanceCorpus.MustPass(scenario))
        {
            Assert.Skip(ex.Message);
        }
    }
}
