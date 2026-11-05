using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Messages;

public sealed class OverriddenMessageDictionaryTests
{
    private static (OverriddenMessageDictionary Dictionary, CollectingReport Report) Load(TempDir dir, string content)
    {
        string file = dir.Combine("messages.tsv");
        File.WriteAllText(file, content);
        CollectingReport report = new(new LocalizedMessageDictionary(System.Globalization.CultureInfo.GetCultureInfo("en")));
        return (new OverriddenMessageDictionary(file, report, System.Globalization.CultureInfo.GetCultureInfo("en")), report);
    }

    [Fact]
    public void Constructor_OfAMissingFile_ReportsChk001()
    {
        // Arrange
        using TempDir dir = new();
        CollectingReport report = new();

        // Act
        OverriddenMessageDictionary dictionary = new(dir.Combine("missing.tsv"), report);

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Equal(MessageId.CHK_001);
        report.Messages[0].Location.Path.Should().EndWith("missing.tsv");
        dictionary.GetMessage(MessageId.OPF_014).Should().Be(LocalizedMessages.GetInstance().GetMessage(MessageId.OPF_014));
    }

    [Fact]
    public void GetMessage_AfterASeverityOverride_KeepsTheTextAndNotesTheChange()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (OverriddenMessageDictionary dictionary, CollectingReport report) = Load(dir, "OPF-014\tWARNING\n");

        // Assert
        Message message = dictionary.GetMessage(MessageId.OPF_014);
        message.Severity.Should().Be(Severity.Warning);
        message.OriginalSeverity.Should().Be(Severity.Error);
        message.Text.Should().Be(" (severity overridden from ERROR) The property \"%1$s\" should be declared in the OPF file.");
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void GetMessage_OfAMessageWithoutATextWhoseSeverityIsRaised_IsNotLeftBlank()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (OverriddenMessageDictionary dictionary, CollectingReport report) = Load(dir, "ACC-001\tWARNING\n");

        // Assert
        Message message = dictionary.GetMessage(MessageId.ACC_001);
        message.Severity.Should().Be(Severity.Warning);
        dictionary.Format(message).Should().Contain("No description is available for ACC-001.");
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void GetMessage_AfterATextOverride_UsesTheNewTextWithoutNotingASeverityChange()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (OverriddenMessageDictionary dictionary, _) = Load(dir, "OPF-014\tERROR\tCustom text about %1$s\n");

        // Assert
        Message message = dictionary.GetMessage(MessageId.OPF_014);
        message.Text.Should().Be("Custom text about %1$s");
        message.Severity.Should().Be(Severity.Error);
        dictionary.Format(message, "nav").Should().Be("Custom text about nav");
    }

    [Fact]
    public void GetMessage_AfterASuggestionOverride_UsesTheNewSuggestion()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (OverriddenMessageDictionary dictionary, _) = Load(dir, "OPF-035\tERROR\t\tMy suggestion\n");

        // Assert
        Message message = dictionary.GetMessage(MessageId.OPF_035);
        message.Suggestion.Should().Be("My suggestion");
        message.Severity.Should().Be(Severity.Error);
        message.OriginalSeverity.Should().Be(Severity.Warning);
        message.Text.Should().Contain("(severity overridden from WARNING)");
    }

    [Fact]
    public void Constructor_WithATextUsingTooManyArguments_ReportsChk004AndKeepsTheMessage()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (OverriddenMessageDictionary dictionary, CollectingReport report) = Load(dir, "OPF-014\tERROR\t%1$s and %2$s\n");

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Equal(MessageId.CHK_004);
        report.Messages[0].Location.Context.Should().Be("%1$s and %2$s");
        dictionary.GetMessage(MessageId.OPF_014).Text.Should().Be("The property \"%1$s\" should be declared in the OPF file.");
    }

    [Fact]
    public void Constructor_WithASuggestionUsingTooManyArguments_ReportsChk005()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (_, CollectingReport report) = Load(dir, "OPF-035\tERROR\t\t%1$s\n");

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Equal(MessageId.CHK_005);
    }

    [Fact]
    public void Constructor_WithAnUnknownId_ReportsChk002WithItsLine()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (_, CollectingReport report) = Load(dir, "XXX-001\tERROR\n");

        // Assert
        ReportedMessage reported = report.Messages.Should().ContainSingle().Subject;
        reported.Message.Id.Should().Be(MessageId.CHK_002);
        reported.Location.Line.Should().Be(1);
        reported.Arguments[0].Should().Be("XXX-001");
        reported.Text.Should().Contain("XXX-001");
    }

    [Fact]
    public void Constructor_WithAnUnknownSeverity_ReportsChk003WithItsColumn()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (_, CollectingReport report) = Load(dir, "OPF-014\tfatalish\n");

        // Assert
        ReportedMessage reported = report.Messages.Should().ContainSingle().Subject;
        reported.Message.Id.Should().Be(MessageId.CHK_003);
        reported.Location.Column.Should().Be(1 + "OPF-014".Length);
        reported.Arguments[0].Should().Be("fatalish");
    }

    [Fact]
    public void Constructor_ReportsTheRealLineOfEachProblem_HeaderAndSkippedLinesIncluded()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (_, CollectingReport report) = Load(dir, "ID\tSeverity\tMessage\tSuggestion\nXXX-001\tERROR\n\nOPF-014\n\nYYY-002\tERROR\n");

        // Assert
        report.Messages.Select(m => m.Location.Line).Should().Equal(2, 6);
    }

    [Fact]
    public void Constructor_AcceptsTheUnderscoreSpellingOfAnId()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (OverriddenMessageDictionary dictionary, CollectingReport report) = Load(dir, "HTM_053\tERROR\n");

        // Assert
        report.Messages.Should().BeEmpty();
        dictionary.GetMessage(MessageId.HTM_053).Severity.Should().Be(Severity.Error);
    }

    [Fact]
    public void Constructor_IgnoresLinesWithASingleField_AndHeadersInAnyCase()
    {
        // Arrange
        using TempDir dir = new();

        // Act
        (OverriddenMessageDictionary dictionary, CollectingReport report) = Load(dir, "Id\tsomething\nOPF-014\n\nOPF-014\t\n");

        // Assert
        report.Messages.Should().BeEmpty();
        dictionary.GetMessage(MessageId.OPF_014).Severity.Should().Be(Severity.Error);
    }

    [Fact]
    public void Constructor_AcceptsTheOutputOfTheDumper_AsItIs()
    {
        // Arrange
        using TempDir dir = new();
        StringWriter dump = new();
        new MessageDictionaryDumper(new LocalizedMessageDictionary(System.Globalization.CultureInfo.GetCultureInfo("en"))).Dump(dump);

        // Act
        (OverriddenMessageDictionary dictionary, CollectingReport report) = Load(dir, dump.ToString());

        // Assert
        report.Messages.Should().BeEmpty();
        MessageIds.All.Select(dictionary.GetMessage).Should().Equal(
            MessageIds.All.Select(id => LocalizedMessages.GetInstance(System.Globalization.CultureInfo.GetCultureInfo("en")).GetMessage(id)));
    }
}
