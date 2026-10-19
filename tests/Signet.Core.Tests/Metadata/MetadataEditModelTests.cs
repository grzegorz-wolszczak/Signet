using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Metadata;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Metadata;

/// <summary>
/// Tests of <see cref="MetadataEditModel"/> — the extraction/writing engine of the "Metadata Editor".
/// </summary>
public sealed class MetadataEditModelTests
{
    private static string ReadCorpusOpf(string corpusDir) =>
        File.ReadAllText(Directory.EnumerateFiles(corpusDir, "*.opf", SearchOption.AllDirectories).Single());

    private static MetadataEntry Element(MetadataEditState state, string code) =>
        state.Elements.Single(e => e.Code == code);

    // ---- EPUB 3: ekstrakcja ----

    [Fact]
    public void Extract_epub3_excludes_unique_identifier_and_keeps_second_identifier()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3RichMetadata));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: true);

        // Assert
        state.Elements.Should().NotContain(e => e.Code == "dc:identifier" && e.Content.Contains("66666666"));
        MetadataEntry isbn = Element(state, "dc:identifier");
        isbn.Content.Should().Be("urn:isbn:9780000000001");
    }

    [Fact]
    public void Extract_epub3_folds_refines_into_children()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3RichMetadata));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: true);

        // Assert
        MetadataEntry title = Element(state, "dc:title");
        title.Content.Should().Be("Rich Metadata");
        title.Children.Select(c => c.Code).Should().Equal("file-as", "id", "title-type");
        title.Children.Single(c => c.Code == "title-type").Content.Should().Be("main");
        title.Children.Single(c => c.Code == "id").Content.Should().Be("title");

        MetadataEntry creator = Element(state, "dc:creator");
        creator.Content.Should().Be("Ada Lovelace");
        creator.Children.Select(c => c.Code).Should().Equal("file-as", "id", "role", "scheme");
        creator.Children.Single(c => c.Code == "role").Content.Should().Be("aut");
        creator.Children.Single(c => c.Code == "scheme").Content.Should().Be("marc:relators");
    }

    [Fact]
    public void Extract_epub3_recognizes_primary_meta_property_as_element()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3RichMetadata));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: true);

        // Assert
        Element(state, "dcterms:modified").Content.Should().Be("2026-01-01T00:00:00Z");
    }

    [Fact]
    public void Extract_epub3_puts_unrecognized_link_into_other_and_round_trips_it()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3RichMetadata));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: true);
        MetadataEditModel.Save(doc, state, isEpub3: true);

        // Assert
        doc.Metadata.Should().ContainSingle(m => m.Name == "link")
            .Which.Attributes.Value("href").Should().Be("records/onix.xml");
    }

    [Fact]
    public void Round_trip_epub3_preserves_ids_and_refines_without_edits()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3RichMetadata));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: true);
        MetadataEditModel.Save(doc, state, isEpub3: true);

        // Assert
        doc.Metadata.Should().ContainSingle(
            m => m.Name == "dc:title" && m.Attributes.Value("id") == "title");
        doc.Metadata.Should().ContainSingle(
            m => m.Name == "meta" && m.Attributes.Value("refines") == "#title" &&
                 m.Attributes.Value("property") == "title-type" && m.Content == "main");
        doc.Metadata.Should().ContainSingle(
            m => m.Name == "meta" && m.Attributes.Value("refines") == "#author" &&
                 m.Attributes.Value("property") == "role" && m.Attributes.Value("scheme") == "marc:relators");

        // Chroniony identyfikator glowny pozostaje nietkniety.
        doc.Metadata.Should().ContainSingle(
            m => m.Name == "dc:identifier" && m.Attributes.Value("id") == "pub-id" &&
                 m.Content.Contains("66666666"));
    }

    [Fact]
    public void Save_epub3_generates_id_and_refines_for_new_element_without_explicit_id()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3Minimal));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: true);

        // Arrange
        MetadataEntry newCreator = new() { Code = "dc:creator", Content = "New Author" };
        newCreator.Children.Add(new MetadataEntry { Code = "role", Content = "aut" });
        newCreator.Children.Add(new MetadataEntry { Code = "scheme", Content = "marc:relators" });
        state.Elements.Add(newCreator);

        // Act
        MetadataEditModel.Save(doc, state, isEpub3: true);

        // Assert
        MetaEntry creator = doc.Metadata.Single(m => m.Name == "dc:creator" && m.Content == "New Author");
        string newId = creator.Attributes.Value("id");
        newId.Should().Be("cre");

        MetaEntry refine = doc.Metadata.Single(
            m => m.Name == "meta" && m.Attributes.Value("refines") == "#" + newId && m.Attributes.Value("property") == "role");
        refine.Content.Should().Be("aut");
        refine.Attributes.Value("scheme").Should().Be("marc:relators");
    }

    [Fact]
    public void Save_epub3_avoids_id_collision_with_existing_ids()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3RichMetadata));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: true);

        // Arrange
        // "cov" is not used yet, so a new element without its own "coverage" should get "cov".
        MetadataEntry coverage = new() { Code = "dc:coverage", Content = "Global" };
        coverage.Children.Add(new MetadataEntry { Code = "display-seq", Content = "1" });
        state.Elements.Add(coverage);

        // Act
        MetadataEditModel.Save(doc, state, isEpub3: true);

        // Assert
        doc.Metadata.Single(m => m.Name == "dc:coverage").Attributes.Value("id").Should().Be("cov");
    }

    // ---- EPUB 2: extraction and writing ----

    [Fact]
    public void Extract_epub2_excludes_unique_identifier_and_keeps_role_attributes()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub2Minimal));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: false);

        // Assert
        state.Elements.Should().NotContain(e => e.Code == "dc:identifier");

        MetadataEntry creator = Element(state, "dc:creator");
        creator.Content.Should().Be("Jane Doe");
        creator.Children.Single(c => c.Code == "opf:role").Content.Should().Be("aut");
        creator.Children.Single(c => c.Code == "opf:file-as").Content.Should().Be("Doe, Jane");
    }

    [Fact]
    public void Round_trip_epub2_preserves_metadata_and_forces_namespaces()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub2Minimal));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: false);
        MetadataEditModel.Save(doc, state, isEpub3: false);

        // Assert
        doc.Metadata.Select(m => m.Name).Should().Contain("dc:title").And.Contain("dc:language").And.Contain("dc:creator");
        doc.Metadata.Single(m => m.Name == "dc:creator").Attributes.Value("opf:role").Should().Be("aut");
        doc.MetadataAttributes.Attributes.Value("xmlns:opf").Should().Be("http://www.idpf.org/2007/opf");
        doc.MetadataAttributes.Attributes.Value("xmlns:dc").Should().Be("http://purl.org/dc/elements/1.1/");
    }

    [Fact]
    public void Save_epub2_writes_unrecognized_element_as_generic_meta_name_content()
    {
        // Arrange
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub2Minimal));

        // Act
        MetadataEditState state = MetadataEditModel.Extract(doc, isEpub3: false);

        // Arrange
        state.Elements.Add(new MetadataEntry { Code = "calibre:series", Content = "The Series" });

        // Act
        MetadataEditModel.Save(doc, state, isEpub3: false);

        // Assert
        MetaEntry generic = doc.Metadata.Single(m => m.Attributes.Value("name") == "calibre:series");
        generic.Name.Should().Be("meta");
        generic.Attributes.Value("content").Should().Be("The Series");
        generic.Content.Should().BeEmpty();
    }

    // ---- ValidId ----

    [Theory]
    [InlineData("cre", new string[0], "cre")]
    [InlineData("cre", new[] { "cre" }, "cre001")]
    [InlineData("cre", new[] { "cre", "cre001" }, "cre002")]
    public void ValidId_avoids_collisions(string id, string[] taken, string expected)
    {
        // Act & Assert
        MetadataEditModel.ValidId(id, taken.ToList()).Should().Be(expected);
    }
}
