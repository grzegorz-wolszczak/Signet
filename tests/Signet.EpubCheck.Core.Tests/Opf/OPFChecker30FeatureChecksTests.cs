using System.Collections.Generic;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Opf;

/// <summary>The checks of an EPUB 3 package document that use what the content documents reported (pagination, semantics, navigation, dictionaries).</summary>
public sealed class OPFChecker30FeatureChecksTests
{
    private static readonly EpubLocation s_somewhere = new("chapter.xhtml", 3, 7);

    [Fact]
    public void Check_PageBreaksWithoutAPageListAndASource_ReportsNav003AndOpf066()
    {
        // Arrange
        FeatureReport features = new();
        features.Report(FeatureKind.PageBreak, s_somewhere);

        // Act
        CollectingReport report = Run(Package(), features, EpubProfile.Edupub);

        // Assert
        Ids(report).Should().Contain([MessageId.NAV_003, MessageId.OPF_066]);
    }

    [Fact]
    public void Check_PageBreaksWithAPageListAndAPaginationSource_ReportsNothing()
    {
        // Arrange
        FeatureReport features = new();
        features.Report(FeatureKind.PageBreak, s_somewhere);
        features.Report(FeatureKind.PageList, s_somewhere);
        string metadata = "<dc:source id=\"src\">urn:isbn:9780000000002</dc:source><meta property=\"source-of\" refines=\"#src\">pagination</meta>";

        // Act
        CollectingReport report = Run(Package(metadata), features, EpubProfile.Edupub);

        // Assert
        Ids(report).Should().NotContain([MessageId.NAV_003, MessageId.OPF_066]);
    }

    [Fact]
    public void Check_PaginationSourceWithoutTheRefinement_ReportsOpf066()
    {
        // Arrange
        FeatureReport features = new();
        features.Report(FeatureKind.PageBreak, s_somewhere);
        features.Report(FeatureKind.PageList, s_somewhere);

        // Act
        CollectingReport report = Run(Package("<dc:source>urn:isbn:9780000000002</dc:source>"), features, EpubProfile.Edupub);

        // Assert
        Ids(report).Should().Contain(MessageId.OPF_066).And.NotContain(MessageId.NAV_003);
    }

    [Fact]
    public void Check_PageBreaksOutsideEdupub_AreNotChecked()
    {
        // Arrange
        FeatureReport features = new();
        features.Report(FeatureKind.PageBreak, s_somewhere);

        // Act
        CollectingReport report = Run(Package(), features, EpubProfile.Default);

        // Assert
        Ids(report).Should().NotContain([MessageId.NAV_003, MessageId.OPF_066]);
    }

    [Fact]
    public void Check_MicrodataWithoutRdfa_ReportsHtm051AtTheFirstMicrodata()
    {
        // Arrange
        FeatureReport features = new();
        features.Report(FeatureKind.HasMicrodata, s_somewhere);
        features.Report(FeatureKind.HasMicrodata, new EpubLocation("other.xhtml", 9, 9));

        // Act
        CollectingReport report = Run(Package(), features, EpubProfile.Edupub);

        // Assert
        report.Messages.Single(message => message.Message.Id == MessageId.HTM_051).Location.Should().Be(s_somewhere);
    }

    [Fact]
    public void Check_EdupubSectionsNotInTheToc_ReportsNav004AtTheFirstTocLink()
    {
        // Arrange
        FeatureReport features = new();
        features.Report(FeatureKind.Sections, s_somewhere);
        features.Report(FeatureKind.Sections, new EpubLocation("chapter.xhtml", 8, 1));
        features.Report(FeatureKind.TocLinks, new EpubLocation("nav.xhtml", 5, 5));

        // Act
        CollectingReport report = Run(Package(), features, EpubProfile.Edupub);

        // Assert
        report.Messages.Single(message => message.Message.Id == MessageId.NAV_004).Location.Should().Be(new EpubLocation("nav.xhtml", 5, 5));
    }

    [Theory]
    [InlineData(FeatureKind.Audio, MessageId.NAV_005)]
    [InlineData(FeatureKind.Figure, MessageId.NAV_006)]
    [InlineData(FeatureKind.Table, MessageId.NAV_007)]
    [InlineData(FeatureKind.Video, MessageId.NAV_008)]
    public void Check_EdupubMediaWithoutItsList_ReportsTheNavMessage(FeatureKind feature, MessageId expected)
    {
        // Arrange
        FeatureReport features = new();
        features.Report(feature, s_somewhere);

        // Act
        CollectingReport report = Run(Package(), features, EpubProfile.Edupub);

        // Assert
        Ids(report).Should().Contain(expected);
    }

    [Theory]
    [InlineData(FeatureKind.Audio, FeatureKind.Loa, MessageId.NAV_005)]
    [InlineData(FeatureKind.Figure, FeatureKind.Loi, MessageId.NAV_006)]
    [InlineData(FeatureKind.Table, FeatureKind.Lot, MessageId.NAV_007)]
    [InlineData(FeatureKind.Video, FeatureKind.Lov, MessageId.NAV_008)]
    public void Check_EdupubMediaWithItsList_ReportsNothing(FeatureKind feature, FeatureKind list, MessageId notExpected)
    {
        // Arrange
        FeatureReport features = new();
        features.Report(feature, s_somewhere);
        features.Report(list, s_somewhere);

        // Act
        CollectingReport report = Run(Package(), features, EpubProfile.Edupub);

        // Assert
        Ids(report).Should().NotContain(notExpected);
    }

    [Fact]
    public void Check_EdupubPublicationType_IsCheckedLikeTheProfile()
    {
        // Arrange
        FeatureReport features = new();
        features.Report(FeatureKind.Table, s_somewhere);

        // Act
        CollectingReport report = Run(Package(), features, EpubProfile.Default, PublicationType.Edupub);

        // Assert
        Ids(report).Should().Contain(MessageId.NAV_007);
    }

    [Fact]
    public void Check_DictionaryContentInAPublicationThatIsNotADictionary_ReportsOpf079()
    {
        // Arrange
        FeatureReport features = new();
        features.Report(FeatureKind.Dictionary, s_somewhere);

        // Act
        CollectingReport report = Run(Package(), features, EpubProfile.Default);

        // Assert
        report.Messages.Single(message => message.Message.Id == MessageId.OPF_079).Location.Should().Be(s_somewhere);
    }

    [Fact]
    public void Check_DictionaryProfileWithoutDictionaryContent_ReportsOpf078()
    {
        // Arrange & Act
        CollectingReport report = Run(Package(), new FeatureReport(), EpubProfile.Dict);

        // Assert
        Ids(report).Should().Contain(MessageId.OPF_078);
    }

    [Fact]
    public void Check_DictionaryCollectionWithoutDictionaryContent_ReportsOpf078()
    {
        // Arrange
        string collection = "<collection role=\"dictionary\"><link href=\"dict.xhtml\"/></collection>";

        // Act
        CollectingReport report = Run(Package(items: "<item id=\"d\" href=\"dict.xhtml\" media-type=\"application/xhtml+xml\"/>", collections: collection), new FeatureReport(), EpubProfile.Default);

        // Assert
        Ids(report).Should().Contain(MessageId.OPF_078);
    }

    [Fact]
    public void Check_DictionaryCollectionWithItsDictionaryContent_DoesNotReportOpf078()
    {
        // Arrange
        string collection = "<collection role=\"dictionary\"><link href=\"dict.xhtml\"/></collection>";
        FeatureReport features = new();
        features.Report(FeatureKind.Dictionary, new EpubLocation("dict.xhtml", 4, 4));

        // Act
        CollectingReport report = Run(Package(items: "<item id=\"d\" href=\"dict.xhtml\" media-type=\"application/xhtml+xml\"/>", collections: collection), features, EpubProfile.Default);

        // Assert
        Ids(report).Should().NotContain(MessageId.OPF_078);
    }

    private static IReadOnlyList<MessageId> Ids(CollectingReport report) => [.. report.Messages.Select(message => message.Message.Id)];

    private static string Package(string metadata = "", string items = "", string collections = "") =>
        "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"uid\" xml:lang=\"en\">"
        + "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>T</dc:title><dc:language>en</dc:language>"
        + "<dc:identifier id=\"uid\">urn:uuid:12345678-1234-1234-1234-123456789abc</dc:identifier>"
        + "<meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta>" + metadata + "</metadata>"
        + "<manifest>" + items + "</manifest><spine/>" + collections + "</package>";

    private static CollectingReport Run(string package, FeatureReport features, EpubProfile profile, params PublicationType[] publicationTypes)
    {
        CollectingReport report = new() { ReportingLevel = Severity.Usage };
        MemoryEpubSource source = new([new KeyValuePair<string, byte[]>("package.opf", Encoding.UTF8.GetBytes(package))]);
        OCFContainer.Builder builder = new(source);
        builder.AddResource(source.Entries[0]);
        OCFContainer container = builder.Build();
        ValidationContext context = new ValidationContext
        {
            Url = container.RootUrl.Resolve("package.opf"),
            Report = report,
            Version = EpubVersion.Version3,
            MimeType = "application/oebps-package+xml",
            Profile = profile,
            PublicationTypes = new HashSet<PublicationType>(publicationTypes),
        }.WithContainer(container) with
        {
            FeatureReport = features,
        };
        new OPFChecker30(context).Check();
        return report;
    }
}
