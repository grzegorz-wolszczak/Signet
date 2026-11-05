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
        report.Messages[0].Text.Should().Contain("U+002A (*)");
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
    public void NormalizeForComparison_ComposedAndDecomposedNames_AreEqual()
    {
        // Arrange
        const string composed = "m\u00FCnchen.xhtml";
        const string decomposed = "mu\u0308nchen.xhtml";

        // Act
        string normalizedComposed = OCFFilenameChecker.NormalizeForComparison(composed);
        string normalizedDecomposed = OCFFilenameChecker.NormalizeForComparison(decomposed);

        // Assert
        normalizedComposed.Should().Be(normalizedDecomposed);
    }

    [Fact]
    public void NormalizeForComparison_SharpSAndSs_AreEqual()
    {
        // Arrange
        const string sharpS = "content_\u00DF.xhtml";
        const string ss = "content_ss.xhtml";

        // Act
        string normalizedSharpS = OCFFilenameChecker.NormalizeForComparison(sharpS);
        string normalizedSs = OCFFilenameChecker.NormalizeForComparison(ss);

        // Assert
        normalizedSharpS.Should().Be(normalizedSs);
    }
}
