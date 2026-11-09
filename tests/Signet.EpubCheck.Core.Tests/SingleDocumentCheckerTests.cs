using System;
using System.Collections.Generic;
using AutoFixture;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests;

public sealed class SingleDocumentCheckerTests
{
    [Theory]
    [InlineData("opf", SingleDocumentKind.PackageDocument)]
    [InlineData("xhtml", SingleDocumentKind.XhtmlContentDocument)]
    [InlineData("svg", SingleDocumentKind.SvgContentDocument)]
    [InlineData("mo", SingleDocumentKind.MediaOverlaysDocument)]
    [InlineData("nav", SingleDocumentKind.NavigationDocument)]
    public void TryParseMode_KnowsTheModesOfTheTool(string mode, SingleDocumentKind expected)
    {
        // Act
        bool parsed = SingleDocumentChecker.TryParseMode(mode, out SingleDocumentKind kind);

        // Assert
        parsed.Should().BeTrue();
        kind.Should().Be(expected);
    }

    [Fact]
    public void TryParseMode_DoesNotKnowAnythingElse()
    {
        // Arrange
        string mode = new Fixture().Create<string>();

        // Act & Assert
        SingleDocumentChecker.TryParseMode(mode, out _).Should().BeFalse();
        SingleDocumentChecker.TryParseMode(null, out _).Should().BeFalse();
        SingleDocumentChecker.TryParseMode("exp", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(SingleDocumentKind.PackageDocument, EpubVersion.Version2, true)]
    [InlineData(SingleDocumentKind.XhtmlContentDocument, EpubVersion.Version2, true)]
    [InlineData(SingleDocumentKind.SvgContentDocument, EpubVersion.Version2, true)]
    [InlineData(SingleDocumentKind.MediaOverlaysDocument, EpubVersion.Version2, false)]
    [InlineData(SingleDocumentKind.NavigationDocument, EpubVersion.Version2, false)]
    [InlineData(SingleDocumentKind.MediaOverlaysDocument, EpubVersion.Version3, true)]
    [InlineData(SingleDocumentKind.NavigationDocument, EpubVersion.Version3, true)]
    public void IsSupported_NeedsEpub3ForTheOverlaysAndTheNavigationDocument(SingleDocumentKind kind, EpubVersion version, bool expected)
    {
        // Act & Assert
        SingleDocumentChecker.IsSupported(kind, version).Should().Be(expected);
    }

    [Fact]
    public void Check_OfADateEndingWithTheDot_ReportsTheReasonWithoutTheDotOfItsOwn()
    {
        // Arrange
        List<object?[]> args = new();
        Moq.Mock<IReport> report = new();
        report.Setup(r => r.Message(MessageId.OPF_053, Moq.It.IsAny<EpubLocation>(), Moq.It.IsAny<object?[]>()))
            .Callback<MessageId, EpubLocation, object?[]>((_, _, a) => args.Add(a));
        string path = UpstreamCorpus.Combine("epub3", "05-package-document", "files", "metadata-date-trailing-dot-warning.opf");

        // Act
        SingleDocumentChecker.Check(path, SingleDocumentKind.PackageDocument, EpubVersion.Version3, EpubProfile.Default, report.Object);

        // Assert
        args.Should().ContainSingle().Which.Should().Equal("2017-06-14T00:00:01.", " No fraction of a second specified");
    }

    [Fact]
    public void Check_OfANavigationDocumentInEpub2_Throws()
    {
        // Arrange
        IReport report = new Moq.Mock<IReport>().Object;

        // Act & Assert
        Action act = () => SingleDocumentChecker.Check("nav.xhtml", SingleDocumentKind.NavigationDocument, EpubVersion.Version2, EpubProfile.Default, report);
        act.Should().Throw<NotSupportedException>();
    }
}
