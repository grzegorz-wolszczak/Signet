using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Conformance.Tests.Harness;
using Xunit;

namespace Signet.EpubCheck.Conformance.Tests.Tests;

public sealed class StepCoverageTests
{
    [Fact]
    public void EveryStepOfEveryUpstreamScenario_HasExactlyOneBinding()
    {
        // Arrange
        StepRegistry registry = StepRegistry.CreateDefault();
        var scenarios = FeatureLoader.LoadAll(ConformanceCorpus.Root);

        // Act
        string[] problems = scenarios
            .SelectMany(s => s.Steps.Select(step => (scenario: s, step)))
            .Select(x =>
            {
                try
                {
                    registry.Resolve(x.step);
                    return null;
                }
                catch (StepBindingException ex)
                {
                    return $"{x.scenario.FeaturePath}: {ex.Message}";
                }
            })
            .OfType<string>()
            .Distinct()
            .ToArray();

        // Assert
        problems.Should().BeEmpty();
    }

    [Fact]
    public void TheCorpus_HoldsTheScenariosOfUpstream5_4_0()
    {
        // Act
        var scenarios = FeatureLoader.LoadAll(ConformanceCorpus.Root);

        // Assert
        scenarios.Select(s => s.FeaturePath).Distinct().Should().HaveCount(50);
        scenarios.Should().HaveCountGreaterThan(1000);
        scenarios.Select(s => s.Id).Should().OnlyHaveUniqueItems();
    }
}
