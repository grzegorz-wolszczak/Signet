using System;
using System.Linq;
using AutoFixture.Xunit3;
using AwesomeAssertions;
using Moq;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Reporting;

public sealed class ReportBaseTests
{
    private static CollectingReport NewReport(Func<MessageId, Severity> severityOf)
    {
        Mock<IMessageDictionary> dictionary = new();
        dictionary.Setup(d => d.GetMessage(It.IsAny<MessageId>()))
            .Returns((MessageId id) => new Message(id, severityOf(id), "text", string.Empty));
        dictionary.Setup(d => d.Format(It.IsAny<Message>(), It.IsAny<object?[]>())).Returns("formatted text");
        return new CollectingReport(dictionary.Object);
    }

    [Theory, AutoData]
    public void Message_CountsEachSeverityOnItsOwnCounter(string path)
    {
        // Arrange
        CollectingReport report = NewReport(id => id switch
        {
            MessageId.CHK_001 => Severity.Error,
            MessageId.CHK_002 => Severity.Warning,
            MessageId.CHK_003 => Severity.Fatal,
            MessageId.CHK_004 => Severity.Info,
            _ => Severity.Usage,
        });
        report.ReportingLevel = Severity.Usage;
        EpubLocation location = new(path);

        // Act
        report.Message(MessageId.CHK_001, location);
        report.Message(MessageId.CHK_001, location);
        report.Message(MessageId.CHK_002, location);
        report.Message(MessageId.CHK_003, location);
        report.Message(MessageId.CHK_004, location);
        report.Message(MessageId.CHK_005, location);

        // Assert
        report.ErrorCount.Should().Be(2);
        report.WarningCount.Should().Be(1);
        report.FatalErrorCount.Should().Be(1);
        report.InfoCount.Should().Be(1);
        report.UsageCount.Should().Be(1);
        report.Messages.Should().HaveCount(6);
    }

    [Theory, AutoData]
    public void Message_BelowTheReportingLevel_IsNeitherCountedNorPassedOn(string path)
    {
        // Arrange
        CollectingReport report = NewReport(id => id == MessageId.CHK_001 ? Severity.Warning : Severity.Usage);
        report.ReportingLevel = Severity.Error;

        // Act
        report.Message(MessageId.CHK_001, new EpubLocation(path));
        report.Message(MessageId.CHK_002, new EpubLocation(path));

        // Assert
        report.WarningCount.Should().Be(0);
        report.UsageCount.Should().Be(0);
        report.Messages.Should().BeEmpty();
    }

    [Theory, AutoData]
    public void Message_ByDefault_ReportsInfoAndAbove_AndSkipsUsage(string path)
    {
        // Arrange
        CollectingReport report = NewReport(id => id == MessageId.CHK_001 ? Severity.Info : Severity.Usage);

        // Act
        report.Message(MessageId.CHK_001, new EpubLocation(path));
        report.Message(MessageId.CHK_002, new EpubLocation(path));

        // Assert
        report.ReportingLevel.Should().Be(Severity.Info);
        report.Messages.Select(m => m.Message.Id).Should().Equal(MessageId.CHK_001);
    }

    [Theory, AutoData]
    public void Message_OfASuppressedMessage_IsNeverReported(string path)
    {
        // Arrange
        CollectingReport report = NewReport(_ => Severity.Suppressed);
        report.ReportingLevel = Severity.Usage;

        // Act
        report.Message(MessageId.ACC_001, new EpubLocation(path));

        // Assert
        report.Messages.Should().BeEmpty();
    }

    [Theory, AutoData]
    public void Message_KeepsTheLocationAndTheArguments(string path, string first, int second)
    {
        // Arrange
        CollectingReport report = NewReport(_ => Severity.Error);
        EpubLocation location = new(path, 2, 5);

        // Act
        report.Message(MessageId.RSC_005, location, first, second);

        // Assert
        ReportedMessage reported = report.Messages.Should().ContainSingle().Subject;
        reported.Location.Should().Be(location);
        reported.Arguments.Should().Equal(first, second);
        reported.Text.Should().Be("formatted text");
    }

    [Theory]
    [InlineData(Severity.Suppressed)]
    [InlineData((Severity)6)]
    [InlineData((Severity)(-1))]
    public void ReportingLevel_OutsideUsageToFatal_Throws(Severity level)
    {
        // Arrange
        CollectingReport report = NewReport(_ => Severity.Error);

        // Act & Assert
        report.Invoking(r => r.ReportingLevel = level).Should().Throw<ArgumentOutOfRangeException>();
        report.ReportingLevel.Should().Be(Severity.Info);
    }

    [Fact]
    public void MaxOfEachMessage_DefaultsTo25()
    {
        // Act
        CollectingReport report = NewReport(_ => Severity.Error);

        // Assert
        report.MaxOfEachMessage.Should().Be(25);
        IReport.DefaultMaxOfEachMessage.Should().Be(25);
    }

    [Theory, AutoData]
    public void Info_KeepsTheFeaturesInOrder(string resource, string value)
    {
        // Arrange
        CollectingReport report = NewReport(_ => Severity.Error);

        // Act
        report.Info(resource, FeatureKind.DcTitle, value);
        report.Info(null, FeatureKind.FormatVersion, null);

        // Assert
        report.Features.Should().Equal(
            new ReportedFeature(resource, FeatureKind.DcTitle, value),
            new ReportedFeature(null, FeatureKind.FormatVersion, null));
    }

    [Fact]
    public void FeatureKinds_ToText_GivesTheTextsOfUpstream()
    {
        // Act & Assert
        FeatureKind.Audio.ToText().Should().Be("audio element");
        FeatureKind.DcLanguage.ToText().Should().Be("language");
        FeatureKind.HasNcx.ToText().Should().Be("Has ncx file");
        FeatureKind.Sha256.ToText().Should().Be("SHA-256");
        Enum.GetValues<FeatureKind>().Should().HaveCount(60);
    }

    [Fact]
    public void SetLocale_SwitchesTheTextsToPolish()
    {
        // Arrange
        CollectingReport report = new();
        report.SetLocale(System.Globalization.CultureInfo.GetCultureInfo("pl"));

        // Act
        report.Message(MessageId.RSC_001, new EpubLocation("a.epub"), "b.xhtml");

        // Assert
        report.Messages.Should().ContainSingle().Which.Text.Should().Be("Nie można znaleźć pliku \"b.xhtml\".");
    }

    [Fact]
    public void SetOverrideFile_OfAMissingFile_ReportsChk001WithTheDictionaryItHad()
    {
        // Arrange
        using Signet.Core.Tests.TestSupport.TempDir dir = new();
        CollectingReport report = new();

        // Act
        report.SetOverrideFile(dir.Combine("missing.tsv"));

        // Assert
        report.Messages.Should().ContainSingle().Which.Message.Id.Should().Be(MessageId.CHK_001);
        report.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void SetOverrideFile_ChangesTheSeverityOfLaterMessages()
    {
        // Arrange
        using Signet.Core.Tests.TestSupport.TempDir dir = new();
        string file = dir.Combine("messages.tsv");
        System.IO.File.WriteAllText(file, "OPF-014\tSUPPRESSED\n");
        CollectingReport report = new();
        report.SetOverrideFile(file);

        // Act
        report.Message(MessageId.OPF_014, new EpubLocation("a.opf"), "nav");

        // Assert
        report.Messages.Should().BeEmpty();
        report.ErrorCount.Should().Be(0);
    }
}
