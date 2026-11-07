using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Dict;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Dict;

/// <summary>The search key map of an EPUB dictionary.</summary>
public sealed class SearchKeyMapCheckerTests
{
    private const string Skm = "application/vnd.epub.search-key-map+xml";

    private const string Map =
        "<search-key-map xmlns=\"http://www.idpf.org/2007/ops\">"
        + "<search-key-group href=\"dict.xhtml#a\"><match value=\"a\"/><match value=\"b\" href=\"dict.xhtml#b\"/></search-key-group>"
        + "</search-key-map>";

    [Fact]
    public void Constructor_ResourceThatIsNotASearchKeyMap_Fails()
    {
        // Arrange
        ContentDocumentHarness harness = new("map.xml", Map, ContentDocumentHarness.Xhtml);
        harness.Build();

        // Act & Assert
        FluentActions.Invoking(() => new SearchKeyMapChecker(harness.Context)).Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void Check_GroupsAndMatchesWithAnHref_AreRegisteredAsSearchKeys()
    {
        // Arrange
        ContentDocumentHarness harness = new ContentDocumentHarness("map.xml", Map, Skm).With("dict.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.References.Select(reference => (System.IO.Path.GetFileName(reference.Url.Path), reference.Type)).Should().Equal(
            ("dict.xhtml", ReferenceType.SearchKey), ("dict.xhtml", ReferenceType.SearchKey));
        harness.Ids.Should().NotContain(MessageId.OPF_080);
    }

    [Fact]
    public void Check_FileNotEndingWithXml_ReportsOpf080()
    {
        // Arrange
        ContentDocumentHarness harness = new ContentDocumentHarness("map.skm", Map, Skm).With("dict.xhtml", ContentDocumentHarness.Xhtml);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.OPF_080);
    }

    [Fact]
    public void Check_MapThatIsNotValid_ReportsASchemaError()
    {
        // Arrange
        ContentDocumentHarness harness = new("map.xml", "<search-key-map xmlns=\"http://www.idpf.org/2007/ops\"><foo/></search-key-map>", Skm);

        // Act
        Check(harness);

        // Assert
        harness.Ids.Should().Contain(MessageId.RSC_005);
    }

    [Fact]
    public void CheckerFactory_GivesTheCheckerToSearchKeyMapsInEpub3Only()
    {
        // Arrange
        ContentDocumentHarness harness = new("map.xml", Map, Skm);
        harness.Build();

        // Act & Assert
        CheckerFactory.NewChecker(harness.Context).Should().BeOfType<SearchKeyMapChecker>();
        CheckerFactory.NewChecker(harness.Context with { Version = EpubVersion.Version2 }).Should().BeOfType<PublicationResourceChecker>();
    }

    private static void Check(ContentDocumentHarness harness)
    {
        harness.Build();
        new SearchKeyMapChecker(harness.Context).Check();
    }
}
