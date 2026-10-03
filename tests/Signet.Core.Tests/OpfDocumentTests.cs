using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests;

/// <summary>
/// Tests for <see cref="OpfDocument"/> — parsing OPF into an in-memory model and serializing it back.
/// </summary>
public sealed class OpfDocumentTests
{
    private static string ReadCorpusOpf(string corpusDir) =>
        File.ReadAllText(Directory.EnumerateFiles(corpusDir, "*.opf", SearchOption.AllDirectories).Single());

    public static TheoryData<string> AllCorpusOpfFiles()
    {
        TheoryData<string> data = new();
        foreach (string file in Directory.EnumerateFiles(CorpusPaths.Root, "*.opf", SearchOption.AllDirectories))
        {
            data.Add(file);
        }

        return data;
    }

    // --- corpus: EPUB 2 ---

    [Fact]
    public void Parses_epub2_package_metadata_manifest_spine_and_guide()
    {
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub2Minimal));

        doc.Version.Should().Be("2.0");
        doc.UniqueIdentifierId.Should().Be("BookId");

        doc.Metadata.Select(m => m.Name)
            .Should().Equal("dc:identifier", "dc:title", "dc:language", "dc:creator");
        doc.Metadata[0].Content.Should().Be("urn:uuid:22222222-2222-4222-8222-222222222222");
        doc.Metadata[3].Attributes.Value("opf:file-as").Should().Be("Doe, Jane");

        doc.Manifest.Select(m => m.Id).Should().Equal("ncx", "css", "ch1");
        doc.Manifest.Select(m => m.Href).Should().Equal("toc.ncx", "Styles/style.css", "Text/chapter1.xhtml");
        doc.Manifest[0].MediaType.Should().Be("application/x-dtbncx+xml");

        doc.SpineAttributes.Attributes.Value("toc").Should().Be("ncx");
        doc.Spine.Should().ContainSingle().Which.IdRef.Should().Be("ch1");

        doc.Guide.Should().ContainSingle();
        doc.Guide[0].Type.Should().Be("text");
        doc.Guide[0].Href.Should().Be("Text/chapter1.xhtml");
    }

    // --- corpus: EPUB 3 ---

    [Fact]
    public void Parses_epub3_manifest_properties_and_spine_toc()
    {
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3WithNcx));

        doc.Version.Should().Be("3.0");
        doc.Manifest.Single(m => m.Id == "nav").Attributes.Value("properties").Should().Be("nav");
        doc.SpineAttributes.Attributes.Value("toc").Should().Be("ncx");
        doc.Spine.Select(s => s.IdRef).Should().Equal("ch1", "ch2");
    }

    [Fact]
    public void Parses_epub3_refinements_multiple_identifiers_and_link()
    {
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3RichMetadata));

        doc.Metadata.Where(m => m.Name == "dc:identifier").Select(m => m.Attributes.Value("id"))
            .Should().Equal("pub-id", "isbn");

        MetaEntry titleType = doc.Metadata.Single(
            m => m.Name == "meta" && m.Attributes.Value("refines") == "#title" &&
                 m.Attributes.Value("property") == "title-type");
        titleType.Content.Should().Be("main");

        doc.Metadata.Single(m => m.Name == "meta" && m.Attributes.Value("property") == "role")
            .Attributes.Value("scheme").Should().Be("marc:relators");

        doc.Metadata.Should().ContainSingle(m => m.Name == "link")
            .Which.Attributes.Value("href").Should().Be("records/onix.xml");

        doc.Manifest.Single(m => m.Id == "ch1").Attributes.Value("properties").Should().Be("scripted mathml");
        doc.Spine[0].Attributes.Value("linear").Should().Be("yes");
        doc.Guide.Should().ContainSingle().Which.Type.Should().Be("toc");
    }

    [Fact]
    public void Comments_in_the_opf_are_ignored_without_error()
    {
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3RichMetadata));

        doc.ToXml().Should().NotContain("<!--");
        doc.Metadata.Should().NotContain(m => m.Name.Contains("!--", StringComparison.Ordinal));
    }

    // --- round-trip ---

    [Theory]
    [MemberData(nameof(AllCorpusOpfFiles))]
    public void Round_trip_parse_to_xml_to_parse_is_stable(string opfFile)
    {
        string source = File.ReadAllText(opfFile);

        string once = OpfDocument.Parse(source).ToXml();
        string twice = OpfDocument.Parse(once).ToXml();

        twice.Should().Be(once);
    }

    [Theory]
    [MemberData(nameof(AllCorpusOpfFiles))]
    public void Round_trip_preserves_manifest_and_spine_order(string opfFile)
    {
        OpfDocument first = OpfDocument.Parse(File.ReadAllText(opfFile));
        OpfDocument second = OpfDocument.Parse(first.ToXml());

        second.Manifest.Select(m => (m.Id, m.Href, m.MediaType))
            .Should().Equal(first.Manifest.Select(m => (m.Id, m.Href, m.MediaType)));
        second.Spine.Select(s => s.IdRef).Should().Equal(first.Spine.Select(s => s.IdRef));
        second.Metadata.Select(m => (m.Name, m.Content)).Should().Equal(first.Metadata.Select(m => (m.Name, m.Content)));
    }

    // --- ToXml serialization ---

    [Fact]
    public void ToXml_uses_the_fixed_layout()
    {
        const string source = """
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">urn:uuid:1</dc:identifier>
                <dc:title>T</dc:title>
              </metadata>
              <manifest>
                <item id="c1" href="a.xhtml" media-type="application/xhtml+xml"/>
              </manifest>
              <spine>
                <itemref idref="c1"/>
              </spine>
            </package>
            """;

        string xml = OpfDocument.Parse(source).ToXml();

        xml.Should().Be(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<package version=\"3.0\" unique-identifier=\"bookid\" xmlns=\"http://www.idpf.org/2007/opf\">\n" +
            "  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:opf=\"http://www.idpf.org/2007/opf\">\n" +
            "    <dc:identifier id=\"bookid\">urn:uuid:1</dc:identifier>\n" +
            "    <dc:title>T</dc:title>\n" +
            "  </metadata>\n" +
            "  <manifest>\n" +
            "    <item id=\"c1\" href=\"a.xhtml\" media-type=\"application/xhtml+xml\"/>\n" +
            "  </manifest>\n" +
            "  <spine>\n" +
            "    <itemref idref=\"c1\"/>\n" +
            "  </spine>\n" +
            "</package>\n");
    }

    [Fact]
    public void ToXml_omits_an_empty_guide()
    {
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3Minimal));

        doc.Guide.Should().BeEmpty();
        doc.ToXml().Should().NotContain("<guide>");
    }

    [Fact]
    public void GetMetadataXml_returns_only_the_metadata_block()
    {
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub2Minimal));

        string md = doc.GetMetadataXml();

        md.Should().StartWith("  <metadata");
        md.Should().EndWith("  </metadata>\n");
        md.Should().Contain("<dc:title>Minimal EPUB 2</dc:title>");
        md.Should().NotContain("<manifest>");
    }

    // --- edge cases ---

    [Fact]
    public void Handles_a_prefixed_package_element_and_rewrites_the_namespace()
    {
        const string source =
            "<opf:package xmlns:opf=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\">" +
            "<opf:metadata><dc:title xmlns:dc=\"http://purl.org/dc/elements/1.1/\">X</dc:title></opf:metadata>" +
            "<opf:manifest><opf:item id=\"a\" href=\"a.xhtml\" media-type=\"application/xhtml+xml\"/></opf:manifest>" +
            "<opf:spine><opf:itemref idref=\"a\"/></opf:spine></opf:package>";

        OpfDocument doc = OpfDocument.Parse(source);

        doc.Version.Should().Be("3.0");
        doc.Package.Attributes.Value("xmlns").Should().Be("http://www.idpf.org/2007/opf");
        doc.Package.Attributes.Contains("xmlns:opf").Should().BeFalse();
        doc.Manifest.Should().ContainSingle().Which.Id.Should().Be("a");
        doc.Spine.Should().ContainSingle().Which.IdRef.Should().Be("a");
    }

    [Fact]
    public void Assigns_sequential_ids_to_manifest_items_without_one()
    {
        const string source =
            "<package version=\"3.0\" unique-identifier=\"id\"><metadata/><manifest>" +
            "<item href=\"a.xhtml\" media-type=\"application/xhtml+xml\"/>" +
            "<item href=\"b.xhtml\" media-type=\"application/xhtml+xml\"/>" +
            "</manifest><spine/></package>";

        OpfDocument doc = OpfDocument.Parse(source);

        doc.Manifest.Select(m => m.Id).Should().Equal("xid000", "xid001");
    }

    [Fact]
    public void Builds_lookup_maps_for_manifest_id_and_href()
    {
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3WithNcx));

        doc.IdToManifestPosition["ch2"].Should().Be(4);
        doc.HrefToManifestPosition["text/chapter2.xhtml"].Should().Be(4);
    }

    [Fact]
    public void RebuildManifestIndex_reflects_manifest_mutation()
    {
        OpfDocument doc = OpfDocument.Parse(ReadCorpusOpf(CorpusPaths.Epub3Minimal));
        int before = doc.Manifest.Count;

        doc.Manifest.Add(new ManifestEntry { Id = "extra", Href = "z.xhtml", MediaType = "application/xhtml+xml" });
        doc.RebuildManifestIndex();

        doc.IdToManifestPosition["extra"].Should().Be(before);
        doc.HrefToManifestPosition["z.xhtml"].Should().Be(before);
    }

    [Fact]
    public void Drops_deprecated_dc_metadata_and_x_metadata_wrappers()
    {
        const string source =
            "<package version=\"2.0\" unique-identifier=\"id\"><metadata>" +
            "<dc-metadata><dc:title xmlns:dc=\"http://purl.org/dc/elements/1.1/\">Y</dc:title></dc-metadata>" +
            "</metadata><manifest/><spine/></package>";

        OpfDocument doc = OpfDocument.Parse(source);

        doc.Metadata.Should().ContainSingle();
        doc.Metadata[0].Name.Should().Be("dc:title");
        doc.Metadata[0].Content.Should().Be("Y");
    }

    [Fact]
    public void Bindings_are_emitted_only_for_non_epub2()
    {
        const string bindings =
            "<metadata/><manifest><item id=\"h\" href=\"h.xhtml\" media-type=\"application/xhtml+xml\"/></manifest>" +
            "<spine/><bindings><mediaType media-type=\"application/x-demo\" handler=\"h\"/></bindings>";

        OpfDocument v3 = OpfDocument.Parse($"<package version=\"3.0\" unique-identifier=\"id\">{bindings}</package>");
        OpfDocument v2 = OpfDocument.Parse($"<package version=\"2.0\" unique-identifier=\"id\">{bindings}</package>");

        v3.Bindings.Should().ContainSingle();
        v3.ToXml().Should().Contain("<bindings>").And.Contain("media-type=\"application/x-demo\"");
        v2.Bindings.Should().ContainSingle();
        v2.ToXml().Should().NotContain("<bindings>");
    }

    [Fact]
    public void Null_source_is_rejected()
    {
        FluentActions.Invoking(() => OpfDocument.Parse(null!))
            .Should().Throw<ArgumentNullException>();
    }
}
