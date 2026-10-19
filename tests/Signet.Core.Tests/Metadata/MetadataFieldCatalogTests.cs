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
        // Act & Assert
        MetadataFieldCatalog.Epub3Elements.Should().HaveCount(26);

        // Act & Assert
        MetadataFieldCatalog.Epub3Elements.Should().ContainKey("dc:creator-aut");

        // Act & Assert
        MetadataFieldCatalog.Epub3Elements["dc:creator-aut"].Name.Should().Be("Author");

        // Act & Assert
        MetadataFieldCatalog.Epub3Elements.Should().ContainKey("belongs-to-collection");

        // Act & Assert
        MetadataFieldCatalog.Epub3Elements.Should().ContainKey("meta");
    }

    [Fact]
    public void Epub3Properties_has_expected_count_and_entries()
    {
        // Act & Assert
        MetadataFieldCatalog.Epub3Properties.Should().HaveCount(22);

        // Act & Assert
        MetadataFieldCatalog.Epub3Properties.Should().ContainKey("role");

        // Act & Assert
        MetadataFieldCatalog.Epub3Properties.Should().ContainKey("title-type:main");

        // Act & Assert
        MetadataFieldCatalog.Epub3Properties.Should().ContainKey("custom-property");
    }

    [Fact]
    public void Epub3XProperties_has_expected_count()
    {
        // Act & Assert
        MetadataFieldCatalog.Epub3XProperties.Should().HaveCount(4);

        // Act & Assert
        MetadataFieldCatalog.Epub3XProperties.Should().ContainKey("title-type");
    }

    [Fact]
    public void Epub2Elements_has_expected_count_and_extension_elements()
    {
        // Act & Assert
        MetadataFieldCatalog.Epub2Elements.Should().HaveCount(28);

        // Act & Assert
        MetadataFieldCatalog.Epub2Elements.Should().ContainKey("calibre:series");

        // Act & Assert
        MetadataFieldCatalog.Epub2Elements.Should().ContainKey("calibre:series_index");

        // Act & Assert
        MetadataFieldCatalog.Epub2Elements.Should().ContainKey("calibre:title_sort");
    }

    [Fact]
    public void Epub2Properties_has_expected_count_and_entries()
    {
        // Act & Assert
        MetadataFieldCatalog.Epub2Properties.Should().HaveCount(8);

        // Act & Assert
        MetadataFieldCatalog.Epub2Properties.Should().ContainKey("opf:scheme-custom");
    }

    [Fact]
    public void Epub2XProperties_has_expected_count_and_entries()
    {
        // Act & Assert
        MetadataFieldCatalog.Epub2XProperties.Should().HaveCount(9);

        // Act & Assert
        MetadataFieldCatalog.Epub2XProperties.Should().ContainKey("DOI");

        // Act & Assert
        MetadataFieldCatalog.Epub2XProperties.Should().ContainKey("opf:event-published");
    }

    [Fact]
    public void RecognizedDcElements_and_IdRootPrefixes_are_consistent()
    {
        // Act & Assert
        MetadataFieldCatalog.RecognizedDcElements.Should().HaveCount(15);

        // Act & Assert
        foreach (string element in MetadataFieldCatalog.RecognizedDcElements)
        {
            MetadataFieldCatalog.IdRootPrefixes.Should().ContainKey(element);
        }
    }

    [Fact]
    public void RecognizedMetaProperties_has_expected_entries()
    {
        // Act & Assert
        MetadataFieldCatalog.RecognizedMetaProperties.Should().Contain("dcterms:modified");

        // Act & Assert
        MetadataFieldCatalog.RecognizedMetaProperties.Should().Contain("belongs-to-collection");

        // Act & Assert
        MetadataFieldCatalog.RecognizedMetaProperties.Should().HaveCount(9);
    }
}
