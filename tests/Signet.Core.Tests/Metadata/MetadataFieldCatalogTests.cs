using AwesomeAssertions;
using Signet.Core.Metadata;
using Xunit;
using Signet.Core.Tests.TestSupport;

namespace Signet.Core.Tests.Metadata;

/// <summary>Sanity tests of the static data of <see cref="MetadataFieldCatalog"/>.</summary>
public sealed class MetadataFieldCatalogTests : EnglishUiCultureTest
{
    [Fact]
    public void Epub3Elements_has_expected_count_and_entries()
    {
        MetadataFieldCatalog.Epub3Elements.Should().HaveCount(26);
        MetadataFieldCatalog.Epub3Elements.Should().ContainKey("dc:creator-aut");
        MetadataFieldCatalog.Epub3Elements["dc:creator-aut"].Name.Should().Be("Author");
        MetadataFieldCatalog.Epub3Elements.Should().ContainKey("belongs-to-collection");
        MetadataFieldCatalog.Epub3Elements.Should().ContainKey("meta");
    }

    [Fact]
    public void Epub3Properties_has_expected_count_and_entries()
    {
        MetadataFieldCatalog.Epub3Properties.Should().HaveCount(22);
        MetadataFieldCatalog.Epub3Properties.Should().ContainKey("role");
        MetadataFieldCatalog.Epub3Properties.Should().ContainKey("title-type:main");
        MetadataFieldCatalog.Epub3Properties.Should().ContainKey("custom-property");
    }

    [Fact]
    public void Epub3XProperties_has_expected_count()
    {
        MetadataFieldCatalog.Epub3XProperties.Should().HaveCount(4);
        MetadataFieldCatalog.Epub3XProperties.Should().ContainKey("title-type");
    }

    [Fact]
    public void Epub2Elements_has_expected_count_and_extension_elements()
    {
        MetadataFieldCatalog.Epub2Elements.Should().HaveCount(28);
        MetadataFieldCatalog.Epub2Elements.Should().ContainKey("calibre:series");
        MetadataFieldCatalog.Epub2Elements.Should().ContainKey("calibre:series_index");
        MetadataFieldCatalog.Epub2Elements.Should().ContainKey("calibre:title_sort");
    }

    [Fact]
    public void Epub2Properties_has_expected_count_and_entries()
    {
        MetadataFieldCatalog.Epub2Properties.Should().HaveCount(8);
        MetadataFieldCatalog.Epub2Properties.Should().ContainKey("opf:scheme-custom");
    }

    [Fact]
    public void Epub2XProperties_has_expected_count_and_entries()
    {
        MetadataFieldCatalog.Epub2XProperties.Should().HaveCount(9);
        MetadataFieldCatalog.Epub2XProperties.Should().ContainKey("DOI");
        MetadataFieldCatalog.Epub2XProperties.Should().ContainKey("opf:event-published");
    }

    [Fact]
    public void RecognizedDcElements_and_IdRootPrefixes_are_consistent()
    {
        MetadataFieldCatalog.RecognizedDcElements.Should().HaveCount(15);
        foreach (string element in MetadataFieldCatalog.RecognizedDcElements)
        {
            MetadataFieldCatalog.IdRootPrefixes.Should().ContainKey(element);
        }
    }

    [Fact]
    public void RecognizedMetaProperties_has_expected_entries()
    {
        MetadataFieldCatalog.RecognizedMetaProperties.Should().Contain("dcterms:modified");
        MetadataFieldCatalog.RecognizedMetaProperties.Should().Contain("belongs-to-collection");
        MetadataFieldCatalog.RecognizedMetaProperties.Should().HaveCount(9);
    }
}
