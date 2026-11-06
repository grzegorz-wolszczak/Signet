using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate;
using Signet.EpubCheck.Core.Tests.Jing;
using Signet.EpubCheck.Core.Xml;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Xml;

/// <summary>
/// The embedded schemas of EPUBCheck: every schema of upstream's <c>XMLValidators</c> loads, the <c>*-30.sch</c> files
/// are preprocessed as upstream's build does it, and the compiled schemas validate documents with upstream's messages.
/// </summary>
public sealed class XmlValidatorsTests
{
    private const string Opf = "http://www.idpf.org/2007/opf";
    private const string DocumentUri = "file:///EPUB/package.opf";

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");

    public static TheoryData<XmlValidators> AllValidators => [.. Enum.GetValues<XmlValidators>()];

    [Theory]
    [MemberData(nameof(AllValidators))]
    public void Get_EverySchema_LoadsOnceAndIsShared(XmlValidators validator)
    {
        // Act
        XmlValidator first = validator.Get();
        XmlValidator second = validator.Get();

        // Assert
        first.Schema.Should().NotBeNull();
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Get_FromManyThreads_ReadsTheSchemaOnce()
    {
        // Act
        XmlValidator[] validators = Enumerable.Range(0, Environment.ProcessorCount * 2).AsParallel()
            .Select(_ => XmlValidators.Nav30Sch.Get()).ToArray();

        // Assert
        validators.Should().AllSatisfy(v => v.Should().BeSameAs(validators[0]));
    }

    [Fact]
    public void Get_OnlyTheInformativeSvgSchema_IsNotNormative()
    {
        // Act
        List<XmlValidators> informative = [.. Enum.GetValues<XmlValidators>().Where(v => !v.Get().IsNormative)];

        // Assert
        informative.Should().Equal(XmlValidators.Svg30InformativeNvdl);
    }

    [Fact]
    public void Get_UndefinedValue_Throws()
    {
        // Arrange
        XmlValidators undefined = (XmlValidators)Enum.GetValues<XmlValidators>().Length;

        // Act & Assert
        undefined.Invoking(v => v.Get()).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_SchemaThatIsNotEmbedded_Throws()
    {
        // Act & Assert
        FluentActions.Invoking(() => new XmlValidator("schema/30/missing.rnc")).Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Validate_ValidPackage_HasNoErrors()
    {
        // Arrange
        string package = Package(Manifest(navProperties: "nav"));

        // Act
        List<SaxParseException> rnc = Validate(XmlValidators.Opf30Rnc, package);
        List<SaxParseException> sch = Validate(XmlValidators.Opf30Sch, package);

        // Assert
        rnc.Should().BeEmpty();
        sch.Should().BeEmpty();
    }

    [Fact]
    public void Validate_PackageWithoutNav_ReportsUpstreamsAssertion()
    {
        // Arrange
        string package = Package(Manifest(navProperties: null));

        // Act
        List<SaxParseException> errors = Validate(XmlValidators.Opf30Sch, package);

        // Assert
        errors.ConvertAll(e => e.Message).Should().Equal(
            "assertion failed:" + Environment.NewLine + "  Exactly one manifest item must declare the \"nav\" property (number of \"nav\" items: 0).");
    }

    [Fact]
    public void Validate_PackageWithUnknownElement_ReportsTheRelaxNgError()
    {
        // Arrange
        string package = Package(Manifest(navProperties: "nav") + "<unknown/>");

        // Act
        List<SaxParseException> errors = Validate(XmlValidators.Opf30Rnc, package);

        // Assert
        errors.Should().ContainSingle().Which.Message.Should().StartWith("element \"unknown\" not allowed anywhere; expected the element end-tag");
    }

    [Fact]
    public void Validate_DuplicateIdInMetadataXml_FiresThePatternOfTheIncludedSchema()
    {
        // Arrange: ocf-metadata-30.sch includes mod/id-unique.sch, resolved by the preprocessing.
        string metadata = "<metadata xmlns=\"http://www.idpf.org/2013/metadata\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
            + "<dc:identifier id=\"a\">urn:x</dc:identifier><dc:title id=\"a\">T</dc:title>"
            + "<meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta></metadata>";

        // Act
        List<SaxParseException> errors = Validate(XmlValidators.Meta30Sch, metadata);

        // Assert
        errors.Should().HaveCount(2).And.AllSatisfy(e => e.Message.Should().Contain("Duplicate \"a\""));
    }

    [Fact]
    public void Source_PreprocessedSchema_HasNoIncludesNorAbstractPatterns()
    {
        // Arrange
        string[] preprocessed = [.. SchemaResources.Paths.Where(SchemaResources.IsPreprocessed).Order(StringComparer.Ordinal)];

        // Act
        Dictionary<string, string> texts = preprocessed.ToDictionary(p => p, Read);

        // Assert
        preprocessed.Should().HaveCount(9).And.Contain("schema/30/epub-xhtml-30.sch").And.NotContain("schema/30/idx/idx-xhtml.sch");
        texts.Values.Should().AllSatisfy(t => t.Should().NotContain("<include").And.NotContain("abstract=\"true\"").And.NotContain("is-a="));
        texts["schema/30/ocf-metadata-30.sch"].Should().Contain("id-unique");
        texts["schema/30/epub-xhtml-30.sch"].Should().Contain("id=\"ancestor-area-map\"");
    }

    [Fact]
    public void Source_SchemaUpstreamDoesNotPreprocess_IsAsEmbedded()
    {
        // Act
        string text = Read("schema/30/idx/idx-xhtml.sch");

        // Assert
        text.Should().Contain("abstract=\"true\"");
    }

    [Fact]
    public void Paths_UseForwardSlashesAndHoldTheCatalogAndTheStylesheets()
    {
        // Act
        List<string> paths = [.. SchemaResources.Paths];

        // Assert
        paths.Should().AllSatisfy(p => p.Should().NotContain("\\"));
        paths.Should().Contain(["schema/30/epub30-catalog.xml", "schema/30/mod/datatypes.rnc", "schema/20/dtd/opf20.dtd",
            "util/sch/iso-sch-include.xsl", "util/sch/iso-sch-abstract.xsl"]);
        paths.Should().NotContain(p => p.EndsWith("LICENSE", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolver_RelativeReference_ResolvesAgainstTheEmbeddedFile()
    {
        // Arrange
        string baseUri = SchemaResources.UriOf("schema/30/dict/search-key-map.rnc");

        // Act
        InputSource source = SchemaResources.Resolver.Resolve("../mod/datatypes.rnc", baseUri);

        // Assert
        source.SystemId.Should().Be("resource:/schema/30/mod/datatypes.rnc");
        using StreamReader reader = new(source.Open());
        reader.ReadToEnd().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Validate_SameSchemaInParallel_GivesEachDocumentItsOwnErrors()
    {
        // Arrange
        string valid = Package(Manifest(navProperties: "nav"));
        string invalid = Package(Manifest(navProperties: null));

        // Act
        List<SaxParseException>[] results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(i => Task.Run(() => Validate(XmlValidators.Opf30Sch, i % 2 == 0 ? valid : invalid))));

        // Assert
        results.Select(r => r.Count).Should().Equal(Enumerable.Range(0, 16).Select(i => i % 2 == 0 ? 0 : 1));
    }

    private static string Read(string path)
    {
        using Stream stream = SchemaResources.Source(path).Open();
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static List<SaxParseException> Validate(XmlValidators validator, string document)
    {
        CollectingErrorHandler errors = new();
        validator.Get().Schema.CreateValidator(errors, s_english).Validate(InputSource.FromString(DocumentUri, document), errors);
        return errors.Errors;
    }

    private static string Manifest(string? navProperties) =>
        "<manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\""
        + (navProperties is null ? string.Empty : " properties=\"" + navProperties + "\"") + "/></manifest>"
        + "<spine><itemref idref=\"nav\"/></spine>";

    private static string Package(string body) =>
        "<package xmlns=\"" + Opf + "\" version=\"3.0\" unique-identifier=\"uid\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">"
        + "<metadata><dc:identifier id=\"uid\">urn:uuid:00000000-0000-0000-0000-000000000000</dc:identifier>"
        + "<dc:title>Title</dc:title><dc:language>en</dc:language>"
        + "<meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta></metadata>"
        + body + "</package>";
}
