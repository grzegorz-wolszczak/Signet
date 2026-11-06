using System.Collections.Generic;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ncx;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ncx;

/// <summary>The NCX of EPUB 2: obsolete in EPUB 3, its identifier, empty labels, and the references it registers.</summary>
public sealed class NCXCheckerTests
{
    private const string Ncx = "application/x-dtbncx+xml";

    [Fact]
    public void Check_ResourceThatIsNotAnNcxByItsType_FailsAtConstruction()
    {
        // Arrange
        ValidationContext context = Context("<ncx/>", out _, mimeType: "text/xml");

        // Act & Assert
        FluentActions.Invoking(() => new NCXChecker(context)).Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void Check_EmptyLabel_ReportsNcx006()
    {
        // Arrange
        ValidationContext context = Context(NcxDocument("urn:uid", "<navPoint id=\"n\" playOrder=\"1\"><navLabel><text> </text></navLabel><content src=\"ch1.xhtml\"/></navPoint>"), out CollectingReport report);

        // Act
        new NCXChecker(context).Check();

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.NCX_006);
    }

    [Fact]
    public void Check_UidWithWhiteSpace_ReportsNcx004()
    {
        // Arrange
        ValidationContext context = Context(NcxDocument(" urn:uid ", string.Empty), out CollectingReport report);

        // Act
        new NCXChecker(context).Check();

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.NCX_004);
    }

    [Theory]
    [InlineData("urn:uid", false)]
    [InlineData("urn:other", true)]
    public void Check_UidThatDiffersFromThePackagesIdentifier_ReportsNcx001(string ncxUid, bool reported)
    {
        // Arrange
        ValidationContext context = Context(NcxDocument(ncxUid, string.Empty), out CollectingReport report);
        context.FeatureReport.Report(FeatureKind.UniqueIdent, new EpubLocation("package.opf"), "urn:uid");

        // Act
        new NCXChecker(context).Check();

        // Assert
        report.Messages.Any(m => m.Message.Id == MessageId.NCX_001).Should().Be(reported);
    }

    [Fact]
    public void Check_NcxInAnEpub3_IsObsolete()
    {
        // Arrange
        ValidationContext context = Context(NcxDocument("urn:uid", string.Empty), out CollectingReport report) with { Version = EpubVersion.Version3 };

        // Act
        new NCXChecker(context).Check();

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.OBS_001);
    }

    [Fact]
    public void Check_ContentLinks_AreRegisteredAsHyperlinks()
    {
        // Arrange
        ValidationContext context = Context(NcxDocument("urn:uid", "<navPoint id=\"n\" playOrder=\"1\"><navLabel><text>T</text></navLabel><content src=\"ch1.xhtml#a\"/></navPoint>"), out _);

        // Act
        new NCXChecker(context).Check();

        // Assert
        context.ReferenceRegistry!.AsList().Should().ContainSingle().Which.Should().Match<Reference>(r =>
            r.Type == ReferenceType.Hyperlink && r.Url.ToString().EndsWith("ch1.xhtml#a", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Check_NcxThatIsNotInTheContainer_ReportsRsc001()
    {
        // Arrange
        ValidationContext context = Context(NcxDocument("urn:uid", string.Empty), out CollectingReport report, ncxName: "other.ncx");

        // Act
        new NCXChecker(context with { Url = context.Container!.RootUrl.Resolve("toc.ncx") }).Check();

        // Assert
        report.Messages.Select(m => m.Message.Id).Should().Contain(MessageId.RSC_001);
    }

    private static string NcxDocument(string uid, string navMap) =>
        "<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\"><head><meta name=\"dtb:uid\" content=\"" + uid + "\"/></head>"
        + "<docTitle><text>Title</text></docTitle><navMap>" + navMap + "</navMap></ncx>";

    private static ValidationContext Context(string ncx, out CollectingReport report, string mimeType = Ncx, string ncxName = "toc.ncx")
    {
        report = new CollectingReport { ReportingLevel = Severity.Usage };
        MemoryEpubSource source = new([new KeyValuePair<string, byte[]>(ncxName, Encoding.UTF8.GetBytes(ncx))]);
        OCFContainer.Builder builder = new(source);
        builder.AddResource(source.Entries[0]);
        OCFContainer container = builder.Build();
        return new ValidationContext
        {
            Url = container.RootUrl.Resolve(ncxName),
            Report = report,
            Version = EpubVersion.Version2,
            MimeType = mimeType,
        }.WithContainer(container);
    }
}
