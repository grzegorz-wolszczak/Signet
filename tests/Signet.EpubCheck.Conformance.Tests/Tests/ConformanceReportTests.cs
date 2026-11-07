using System.Collections.Generic;
using AwesomeAssertions;
using Signet.EpubCheck.Conformance.Tests.Harness;
using Xunit;

namespace Signet.EpubCheck.Conformance.Tests.Tests;

public sealed class ConformanceReportTests
{
    private static ScenarioDefinition Scenario(string feature, string name, int line) =>
        new(feature, "Feature", null, name, [], [], line);

    [Fact]
    public void Render_CountsPerFeature_AndListsFailuresAndSkipsWithTheirReasons()
    {
        // Arrange
        List<ScenarioResult> results =
        [
            new(Scenario("a/one.feature", "ok", 3), ScenarioOutcome.Passed, null),
            new(Scenario("a/one.feature", "broken", 9), ScenarioOutcome.Failed, "Expected 1\nbut found 2"),
            new(Scenario("b/two.feature", "later", 5), ScenarioOutcome.Skipped, "Needs task 9"),
            new(Scenario("b/two.feature", "also later", 7), ScenarioOutcome.Skipped, "Needs task 9"),
        ];

        // Act
        string report = ConformanceReport.Render(results);

        // Assert
        report.Should().Contain("**4 scenarios: 1 passed, 2 skipped, 1 failed.**");
        report.Should().Contain("| `a/one.feature` | 1 | 0 | 1 | 2 |");
        report.Should().Contain("| `b/two.feature` | 0 | 2 | 0 | 2 |");
        report.Should().Contain("- `a/one.feature:9` broken: Expected 1 but found 2");
        report.Should().Contain("### Needs task 9 (2)");
        report.Should().Contain("- `b/two.feature`: 5, 7");
    }

    [Fact]
    public void Render_OfARunWithoutFailures_HasNoFailedSection()
    {
        // Arrange
        List<ScenarioResult> results = [new(Scenario("a/one.feature", "ok", 3), ScenarioOutcome.Passed, null)];

        // Act
        string report = ConformanceReport.Render(results);

        // Assert
        report.Should().NotContain("## Failed scenarios").And.NotContain("## Skipped scenarios");
    }

    [Fact]
    public void Outcomes_GiveEveryScenarioAResult_AndTheOnesThatDidNotRunAreSkipped()
    {
        // Arrange
        ScenarioDefinition ran = Scenario("zz-test/ran.feature", "ran", 1);
        ScenarioDefinition notRun = Scenario("zz-test/not-run.feature", "not run", 2);
        ScenarioOutcomes.Record(ran, ScenarioOutcome.Passed);

        // Act
        IReadOnlyList<ScenarioResult> all = ScenarioOutcomes.All([ran, notRun], "not enabled");

        // Assert
        all.Should().HaveCount(2);
        all[0].Outcome.Should().Be(ScenarioOutcome.Passed);
        all[1].Outcome.Should().Be(ScenarioOutcome.Skipped);
        all[1].Reason.Should().Be("not enabled");
    }
}
