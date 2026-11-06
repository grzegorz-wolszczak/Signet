using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

public sealed class OCFFilenameCheckerTests
{
    [Fact]
    public void Check_FileNameHasRepeatedForbiddenCharacter_ReportsItOnce()
    {
        // Arrange
        CollectingReport report = new();

        // Act
        OCFFilenameChecker.Check("a*name*.xhtml", EpubVersion.Version3, report);

        // Assert
        report.Messages.Should().ContainSingle();
        report.Messages[0].Message.Id.Should().Be(MessageId.PKG_009);
        report.Messages[0].Text.Should().Contain("U+002A (*)").And.NotContain("(*), U+002A");
    }

    [Fact]
    public void Check_VerticalLineInEpub2_DoesNotReportAnError()
    {
        // Arrange
        CollectingReport report = new();

        // Act
        OCFFilenameChecker.Check("a|name.xhtml", EpubVersion.Version2, report);

        // Assert
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void Check_UnknownVersion_UsesTheRulesOfEpub3()
    {
        // Arrange
        CollectingReport report = new();

        // Act
        OCFFilenameChecker.Check("a|name.xhtml", EpubVersion.Unknown, report);

        // Assert
        report.Messages.Should().ContainSingle(message => message.Message.Id == MessageId.PKG_009);
    }

    [Fact]
    public void Check_NonAsciiCharactersInSeveralSegments_ReportsThePathOnce()
    {
        // Arrange
        CollectingReport report = new() { ReportingLevel = Severity.Usage }; // PKG-012 is a usage
        const string path = "EPUB/résumé/café.xhtml";

        // Act
        OCFFilenameChecker.Check(path, EpubVersion.Version3, report);

        // Assert
        report.Messages.Should().ContainSingle();
        report.Messages[0].Message.Id.Should().Be(MessageId.PKG_012);
        report.Messages[0].Text.Should().Contain(path);
    }

    [Fact]
    public void Check_FullStopEndingADirectoryButNotThePath_IsNotReported()
    {
        // Arrange (upstream checks the last character of the whole path only)
        CollectingReport report = new();

        // Act
        OCFFilenameChecker.Check("EPUB/dir./file.xhtml", EpubVersion.Version3, report);

        // Assert
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void Check_FullStopEndingThePath_ReportsThePath()
    {
        // Arrange
        CollectingReport report = new();

        // Act
        OCFFilenameChecker.Check("EPUB/file.", EpubVersion.Version3, report);

        // Assert
        report.Messages.Should().ContainSingle(message => message.Message.Id == MessageId.PKG_011);
    }

    [Theory]
    [InlineData(0xFFFC, "U+FFFC OBJECT REPLACEMENT CHARACTER (SPECIALS)")]
    [InlineData(0xFFF9, "U+FFF9 INTERLINEAR ANNOTATION ANCHOR (SPECIALS)")]
    [InlineData(0xFFF0, "U+FFF0 (SPECIALS)")]
    [InlineData(0xFFFF, "U+FFFF (NON CHARACTER)")]
    public void Check_SpecialCharacter_DescribesItAsIcuDoes(int codePoint, string expected)
    {
        // Arrange
        CollectingReport report = new();

        // Act
        OCFFilenameChecker.Check("a" + char.ConvertFromUtf32(codePoint), EpubVersion.Version3, report);

        // Assert
        report.Messages.Single(message => message.Message.Id == MessageId.PKG_009).Text.Should().Contain(expected);
    }

    [Fact]
    public void Check_GivenALocation_ReportsThere()
    {
        // Arrange
        CollectingReport report = new();
        EpubLocation location = new("book.epub");

        // Act
        OCFFilenameChecker.Check("a name.xhtml", EpubVersion.Version3, report, location);

        // Assert
        report.Messages.Should().ContainSingle().Which.Location.Should().Be(location);
    }
}
