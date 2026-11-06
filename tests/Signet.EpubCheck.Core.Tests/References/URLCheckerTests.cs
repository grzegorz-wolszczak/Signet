using System.Collections.Generic;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.References;

/// <summary>EPUBCheck's check of the URLs of a document of the container (upstream's <c>URLChecker</c>).</summary>
public sealed class URLCheckerTests
{
    private static readonly EpubLocation s_location = new("EPUB/a.xhtml", 3, 4);

    [Fact]
    public void CheckUrl_RelativeUrl_ResolvesAgainstTheDocumentInTheContainer()
    {
        // Arrange
        (URLChecker checker, OCFContainer container, CollectingReport report) = Checker("EPUB/a.xhtml");

        // Act
        Url? url = checker.CheckUrl("images/c.png#x", s_location);

        // Assert
        url.Should().NotBeNull();
        container.Relativize(url!).Should().Be("EPUB/images/c.png#x");
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void CheckUrl_UrlWithAQuery_ReportsRsc033AndDropsIt()
    {
        // Arrange
        (URLChecker checker, OCFContainer container, CollectingReport report) = Checker("EPUB/a.xhtml");

        // Act
        Url? url = checker.CheckUrl("c.png?v=1", s_location);

        // Assert
        container.Relativize(url!).Should().Be("EPUB/c.png");
        report.Messages.Should().ContainSingle().Which.Message.Id.Should().Be(MessageId.RSC_033);
    }

    [Fact]
    public void CheckUrl_UrlThatLeavesTheContainer_ReportsRsc026()
    {
        // Arrange
        (URLChecker checker, _, CollectingReport report) = Checker("EPUB/a.xhtml");

        // Act
        checker.CheckUrl("../../../outside.png", s_location);

        // Assert
        report.Messages.Should().ContainSingle().Which.Message.Id.Should().Be(MessageId.RSC_026);
    }

    [Fact]
    public void CheckUrl_FileUrl_ReportsRsc030()
    {
        // Arrange
        (URLChecker checker, _, CollectingReport report) = Checker("EPUB/a.xhtml");

        // Act
        checker.CheckUrl("file:///c:/a.png", s_location);

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.RSC_030);
    }

    [Fact]
    public void CheckUrl_RemoteUrl_IsNotReportedAsLeavingTheContainer()
    {
        // Arrange
        (URLChecker checker, _, CollectingReport report) = Checker("EPUB/a.xhtml");

        // Act
        Url? url = checker.CheckUrl("https://example.org/a.png?x=1", s_location);

        // Assert
        url!.ToString().Should().Be("https://example.org/a.png?x=1");
        report.Messages.Should().BeEmpty();
    }

    [Fact]
    public void CheckUrl_UrlThatDoesNotParse_ReportsRsc020AndReturnsNull()
    {
        // Arrange
        (URLChecker checker, _, CollectingReport report) = Checker("EPUB/a.xhtml");

        // Act
        Url? url = checker.CheckUrl("http://[::1", s_location);

        // Assert
        url.Should().BeNull();
        report.Messages.Should().ContainSingle().Which.Message.Id.Should().Be(MessageId.RSC_020);
    }

    [Fact]
    public void CheckUrl_Null_IsNull()
    {
        // Arrange
        (URLChecker checker, _, _) = Checker("EPUB/a.xhtml");

        // Act & Assert
        checker.CheckUrl(null, s_location).Should().BeNull();
    }

    [Fact]
    public void SetBase_NewBase_ChangesWhatFollowingUrlsResolveAgainst()
    {
        // Arrange
        (URLChecker checker, OCFContainer container, _) = Checker("EPUB/a.xhtml");

        // Act
        checker.SetBase("sub/", s_location);
        Url? url = checker.CheckUrl("c.png", s_location);

        // Assert
        container.Relativize(url!).Should().Be("EPUB/sub/c.png");
    }

    private static (URLChecker Checker, OCFContainer Container, CollectingReport Report) Checker(string documentPath)
    {
        using MemoryEpubSource source = new([new KeyValuePair<string, byte[]>(documentPath, Encoding.UTF8.GetBytes("<a/>"))]);
        OCFContainer.Builder builder = new(source);
        builder.AddResource(source.Entries[0]);
        OCFContainer container = builder.Build();
        CollectingReport report = new();
        ValidationContext context = new()
        {
            Url = OCFMetaFileUrl(container, documentPath),
            Report = report,
            Container = container,
        };
        return (new URLChecker(context), container, report);
    }

    private static Url OCFMetaFileUrl(OCFContainer container, string path) => container.RootUrl.Resolve(path);
}
