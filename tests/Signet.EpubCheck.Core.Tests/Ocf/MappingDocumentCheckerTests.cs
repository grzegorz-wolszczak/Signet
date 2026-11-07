using AwesomeAssertions;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Tests.TestSupport;
using Signet.EpubCheck.Core.Vocab;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Ocf;

/// <summary>The mapping document of the renditions of a publication.</summary>
public sealed class MappingDocumentCheckerTests
{
    private const string Valid =
        "<html xmlns:epub=\"http://www.idpf.org/2007/ops\" xmlns=\"http://www.w3.org/1999/xhtml\"><head><meta charset=\"utf-8\"/>"
        + "<meta name=\"epub.multiple.renditions.version\" content=\"1.0\"/></head><body><nav epub:type=\"resource-map\"><ul>"
        + "<li><a href=\"EPUB/package.opf#epubcfi(/6/2[c1]!/4/2/1)\"></a></li><li><a href=\"OTHER/package.opf#epubcfi(/6/2[c1]!/4/2/1)\"></a></li>"
        + "</ul></nav></body></html>";

    private const string NoVersion =
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><meta charset=\"utf-8\"/></head><body>"
        + "<nav xmlns:epub=\"http://www.idpf.org/2007/ops\" epub:type=\"resource-map\"><ul><li><a href=\"EPUB/package.opf#epubcfi(/6/2)\"></a></li></ul></nav></body></html>";

    [Fact]
    public void Check_ValidMappingDocument_ReportsNothing()
    {
        // Arrange
        ContentDocumentHarness harness = Mapping(Valid);

        // Act
        new MappingDocumentChecker(harness.Context).Check();

        // Assert
        harness.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Check_MappingDocumentWithoutAVersion_ReportsASchemaError()
    {
        // Arrange
        ContentDocumentHarness harness = Mapping(NoVersion);

        // Act
        new MappingDocumentChecker(harness.Context).Check();

        // Assert
        harness.Ids.Should().Contain(MessageId.RSC_005);
    }

    [Fact]
    public void Check_WithoutTheRenditionMappingProperty_ChecksNothing()
    {
        // Arrange
        ContentDocumentHarness harness = new("mapping.xhtml", NoVersion);
        harness.Build();

        // Act
        new MappingDocumentChecker(harness.Context).Check();

        // Assert
        harness.Ids.Should().BeEmpty();
    }

    private static ContentDocumentHarness Mapping(string content)
    {
        ContentDocumentHarness harness = new("mapping.xhtml", content);
        harness.Build();
        harness.Context.Properties.Should().NotContain(EpubCheckVocab.Vocab.Get(EpubCheckVocab.Property.RenditionMapping));
        harness.WithContext(harness.Context.WithProperty(EpubCheckVocab.Vocab.Get(EpubCheckVocab.Property.RenditionMapping)));
        return harness;
    }
}
