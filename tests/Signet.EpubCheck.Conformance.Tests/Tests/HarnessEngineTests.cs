using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Signet.EpubCheck.Conformance.Tests.Harness;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Conformance.Tests.Tests;

/// <summary>The harness itself, on small scenarios of its own and a checker that reports what the test tells it to.</summary>
public sealed class HarnessEngineTests
{
    private const string Header = """
        Feature: A feature
          Background:
            Given EPUB test files located at '/files/'
            And EPUBCheck with default settings

        """;

    private sealed class ScriptedChecker : IEpubChecker
    {
        private readonly Action<CheckRequest> _script;

        public ScriptedChecker(Action<CheckRequest> script) => _script = script;

        public List<CheckRequest> Requests { get; } = [];

        public void Check(CheckRequest request)
        {
            Requests.Add(request);
            _script(request);
        }
    }

    private static (TempDir Dir, ScenarioWorld World, ScriptedChecker Checker) Arrange(Action<CheckRequest> script)
    {
        TempDir dir = new();
        Directory.CreateDirectory(dir.Combine("files"));
        File.WriteAllText(dir.Combine("files", "book.epub"), "x");
        ScriptedChecker checker = new(script);
        return (dir, new ScenarioWorld(dir.Path, checker), checker);
    }

    private static void Run(string scenarioText, ScenarioWorld world)
    {
        ScenarioDefinition scenario = FeatureLoader.Load(Header + scenarioText, "test.feature").Single();
        ScenarioRunner.Run(scenario, StepRegistry.CreateDefault(), world);
    }

    private static readonly EpubLocation Somewhere = new("OEBPS/a.xhtml", 3, 7);

    [Fact]
    public void AScenario_AssertingExactlyWhatWasReported_Passes()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, ScriptedChecker checker) = Arrange(r =>
        {
            r.Report.Message(MessageId.OPF_014, Somewhere, "nav");
            r.Report.Message(MessageId.OPF_035, Somewhere);
            r.Report.Message(MessageId.ACC_001, Somewhere);
        });
        using TempDir cleanup = dir;

        // Act
        Action act = () => Run("""
              Scenario: Everything is asserted
                When checking EPUB 'book.epub'
                Then error OPF-014 is reported
                And the message contains 'The property "nav" should be declared'
                And the message has line and column info
                And warning OPF-035 is reported
                And no other errors or warnings are reported
                And no usages are reported
            """, world);

        // Assert
        act.Should().NotThrow();
        checker.Requests.Should().ContainSingle().Which.Mode.Should().Be(CheckerMode.Epub);
    }

    [Fact]
    public void AScenario_LeavingAnErrorUnasserted_FailsAtNoOtherErrors()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, _) = Arrange(r =>
        {
            r.Report.Message(MessageId.OPF_014, Somewhere, "nav");
            r.Report.Message(MessageId.RSC_001, Somewhere, "x.png");
        });
        using TempDir cleanup = dir;

        // Act
        Action act = () => Run("""
              Scenario: One error is not asserted
                When checking EPUB 'book.epub'
                Then error OPF-014 is reported
                And no other errors or warnings are reported
            """, world);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*no other errors or warnings are reported*");
    }

    [Fact]
    public void AScenario_AssertingTheWrongSeverity_Fails()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, _) = Arrange(r => r.Report.Message(MessageId.OPF_014, Somewhere, "nav"));
        using TempDir cleanup = dir;

        // Act
        Action act = () => Run("""
              Scenario: Wrong severity
                When checking EPUB 'book.epub'
                Then warning OPF-014 is reported
            """, world);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AScenario_AssertingAMessageThatWasNotReported_Fails()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, _) = Arrange(_ => { });
        using TempDir cleanup = dir;

        // Act
        Action act = () => Run("""
              Scenario: Nothing reported
                When checking EPUB 'book.epub'
                Then error OPF-014 is reported
            """, world);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*OPF-014*");
    }

    [Fact]
    public void AScenario_CountingMessages_ComparesTheQuantity()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, _) = Arrange(r =>
        {
            r.Report.Message(MessageId.RSC_001, Somewhere, "a");
            r.Report.Message(MessageId.RSC_001, Somewhere, "b");
        });
        using TempDir cleanup = dir;

        // Act
        Action twice = () => Run("""
              Scenario: Two
                When checking EPUB 'book.epub'
                Then error RSC-001 is reported 2 times
                And no other errors or warnings are reported
            """, world);

        // Assert
        twice.Should().NotThrow();
    }

    [Fact]
    public void AScenario_ListingTheMessages_ComparesThemInOrder()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, _) = Arrange(r =>
        {
            r.Report.Message(MessageId.RSC_001, Somewhere, "first.png");
            r.Report.Message(MessageId.OPF_014, Somewhere, "nav");
        });
        using TempDir cleanup = dir;

        // Act
        Action act = () => Run("""
              Scenario: A list
                When checking EPUB 'book.epub'
                Then the following errors are reported
                  | RSC-001 | first.png |
                  | OPF-014 |           |
            """.Replace("|           |", "| property  |", StringComparison.Ordinal), world);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AScenario_SettingTheReportingLevelAndLocale_ChangesWhatIsReported()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, _) = Arrange(r =>
        {
            r.Report.Message(MessageId.ACC_009, Somewhere);
            r.Report.Message(MessageId.RSC_001, Somewhere, "a.png");
        });
        using TempDir cleanup = dir;

        // Act
        Action act = () => Run("""
              Scenario: Usages and Polish
                Given the reporting level is set to USAGE
                And the reporting locale is set to 'pl'
                When checking EPUB 'book.epub'
                Then usage ACC-009 is reported
                And error RSC-001 is reported
                And the message is 'Nie można znaleźć pliku "a.png".'
            """, world);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AScenario_OnAMissingFile_Fails()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, ScriptedChecker checker) = Arrange(_ => { });
        using TempDir cleanup = dir;

        // Act
        Action act = () => Run("""
              Scenario: Missing
                When checking EPUB 'nothing.epub'
                Then no errors or warnings are reported
            """, world);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Couldn’t find EPUB*");
        checker.Requests.Should().BeEmpty();
    }

    [Fact]
    public void AScenario_WithoutAChecker_IsNotImplemented_NotFailed()
    {
        // Arrange
        using TempDir dir = new();
        Directory.CreateDirectory(dir.Combine("files"));
        File.WriteAllText(dir.Combine("files", "book.epub"), "x");
        ScenarioWorld world = new(dir.Path, new UnavailableEpubChecker());

        // Act
        Action act = () => Run("""
              Scenario: No checker yet
                When checking EPUB 'book.epub'
                Then no errors or warnings are reported
            """, world);

        // Assert
        act.Should().Throw<ScenarioNotImplementedException>().WithMessage("*not implemented yet*");
    }

    [Fact]
    public void AScenario_CheckingADocument_UsesTheModeOfItsExtension_AndTheConfiguration()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, ScriptedChecker checker) = Arrange(_ => { });
        using TempDir cleanup = dir;
        File.WriteAllText(dir.Combine("files", "package.opf"), "x");

        // Act
        Run("""
              Scenario: A package document of EPUB 2 with a profile
                Given EPUBCheck configured to check EPUB 2.0.1 rules
                And EPUBCheck configured with the 'edupub' profile
                When checking file 'package.opf'
            """, world);

        // Assert
        CheckRequest request = checker.Requests.Should().ContainSingle().Subject;
        request.Mode.Should().Be(CheckerMode.PackageDocument);
        request.Version.Should().Be(Signet.EpubCheck.Core.EpubVersion.Version2);
        request.Profile.Should().Be(Signet.EpubCheck.Core.EpubProfile.Edupub);
        request.Path.Should().EndWith("package.opf");
    }

    [Fact]
    public void AnUndefinedStep_IsReportedAsABindingProblem()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, _) = Arrange(_ => { });
        using TempDir cleanup = dir;

        // Act
        Action act = () => Run("""
              Scenario: Nonsense
                When something nobody wrote a step for
            """, world);

        // Assert
        act.Should().Throw<StepBindingException>().WithMessage("Undefined step*");
    }

    [Fact]
    public void ABackgroundAndAScenarioOutline_AreExpanded()
    {
        // Act
        IReadOnlyList<ScenarioDefinition> scenarios = FeatureLoader.Load(Header + """
              Scenario Outline: Check <name>
                When checking EPUB '<name>.epub'
                Then error <id> is reported

                Examples:
                  | name | id      |
                  | one  | OPF-014 |
                  | two  | RSC-001 |
            """, "test.feature");

        // Assert
        scenarios.Select(s => s.Name).Should().Equal("Check one", "Check two");
        scenarios[0].Steps.Select(s => s.Text).Should().Equal(
            "EPUB test files located at '/files/'",
            "EPUBCheck with default settings",
            "checking EPUB 'one.epub'",
            "error OPF-014 is reported");
        scenarios[1].Steps[^1].Text.Should().Be("error RSC-001 is reported");
    }

    [Fact]
    public void ARuleAndTags_AreKept()
    {
        // Act
        IReadOnlyList<ScenarioDefinition> scenarios = FeatureLoader.Load("""
            @feature
            Feature: A feature
              Rule: A rule
                @spec @output:out/file.json
                Scenario: Inside the rule
                  When checking EPUB 'a.epub'
            """, "dir/test.feature");

        // Assert
        ScenarioDefinition scenario = scenarios.Should().ContainSingle().Subject;
        scenario.Rule.Should().Be("A rule");
        scenario.Tags.Should().Equal("@feature", "@spec", "@output:out/file.json");
        scenario.Id.Should().Be("dir/test.feature:5");
    }

    [Fact]
    public void TheFilesNamedByOutputTags_AreDeletedAfterTheScenario()
    {
        // Arrange
        (TempDir dir, ScenarioWorld world, _) = Arrange(_ => { });
        using TempDir cleanup = dir;
        string produced = dir.Combine("files", "out.txt");
        File.WriteAllText(produced, "x");

        // Act
        Run("""
              @output:out.txt
              Scenario: Produces a file
                Given EPUBCheck with default settings
            """, world);

        // Assert
        File.Exists(produced).Should().BeFalse();
    }
}
