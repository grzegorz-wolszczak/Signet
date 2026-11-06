using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate;
using Signet.EpubCheck.Core.Jing.Validate.Nvdl;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>The messages of NVDL schemas and validation, worded as Jing words them, and the dispatching to RELAX NG.</summary>
public sealed class NvdlMessageTests
{
    private const string Nvdl = "http://purl.oclc.org/dsdl/nvdl/ns/structure/1.0";
    private const string SchemaUri = "file:///schemas/test.nvdl";
    private const string NsA = "http://www.example.org/a";
    private const string NsC = "http://www.example.org/c";

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Validate_ElementOfANamespaceWithoutRule_IsRejected()
    {
        // Arrange
        ISchema schema = Load(Rules("<namespace ns=\"" + NsA + "\"><allow/></namespace>"));

        // Act
        List<SaxParseException> errors = Validate(schema, "<foo xmlns=\"" + NsA + "\">\n<bar xmlns=\"" + NsC + "\"/></foo>");

        // Assert
        errors.Should().ContainSingle().Which.Message.Should().Be("elements from namespace \"" + NsC + "\" are not allowed");
        errors[0].LineNumber.Should().Be(2);
    }

    [Fact]
    public void Validate_AttributesOfARejectedNamespace_AreRejected()
    {
        // Arrange
        ISchema schema = Load(Rules("<namespace ns=\"" + NsA + "\"><allow/></namespace><namespace ns=\"" + NsC + "\" match=\"attributes\"><reject/></namespace>"));

        // Act
        List<SaxParseException> errors = Validate(schema, "<foo xmlns=\"" + NsA + "\" xmlns:c=\"" + NsC + "\" c:x=\"1\"/>");

        // Assert
        errors.Should().ContainSingle().Which.Message.Should().Be("attributes from namespace \"" + NsC + "\" are not allowed");
    }

    [Fact]
    public void Validate_SectionValidatedWithRelaxNg_GivesTheRelaxNgErrors()
    {
        // Arrange
        Dictionary<string, string> documents = new()
        {
            ["file:///schemas/a.rnc"] = "default namespace = \"" + NsA + "\"\nelement foo { element bar { empty } }",
        };
        ISchema schema = Load(Rules("<namespace ns=\"" + NsA + "\"><validate schema=\"a.rnc\" schemaType=\"application/relax-ng-compact-syntax\"/></namespace>"), documents);

        // Act
        List<SaxParseException> errors = Validate(schema, "<foo xmlns=\"" + NsA + "\"><baz/></foo>");

        // Assert
        errors.ConvertAll(e => e.Message).Should().Equal(
            "element \"baz\" not allowed anywhere; expected element \"bar\"",
            "element \"foo\" incomplete; missing required element \"bar\"");
    }

    [Fact]
    public void Validate_ForeignSectionAttachedToItsParent_IsValidatedByTheParentsSchema()
    {
        // Arrange
        Dictionary<string, string> documents = new()
        {
            ["file:///schemas/a.rnc"] = "default namespace = \"" + NsA + "\"\nnamespace c = \"" + NsC + "\"\nelement foo { element c:x { empty } }",
        };
        ISchema schema = Load("<rules xmlns=\"" + Nvdl + "\" startMode=\"m\"><mode name=\"m\"><namespace ns=\"" + NsA
            + "\"><validate schema=\"a.rnc\" schemaType=\"application/relax-ng-compact-syntax\" useMode=\"attach\"/></namespace></mode>"
            + "<mode name=\"attach\"><anyNamespace><attach/></anyNamespace></mode></rules>", documents);

        // Act
        List<SaxParseException> valid = Validate(schema, "<foo xmlns=\"" + NsA + "\"><x xmlns=\"" + NsC + "\"/></foo>");
        List<SaxParseException> invalid = Validate(schema, "<foo xmlns=\"" + NsA + "\"><y xmlns=\"" + NsC + "\"/></foo>");

        // Assert
        valid.Should().BeEmpty();
        // As in Jing, the namespace declarations of an attached section do not reach the parent's validator: it names
        // the expected element with a prefix of its own.
        invalid.ConvertAll(e => e.Message).Should().Equal(
            "element \"y\" not allowed anywhere; expected element \"ns:x\" (with xmlns:ns=\"" + NsC + "\")",
            "element \"foo\" incomplete; missing required element \"ns:x\" (with xmlns:ns=\"" + NsC + "\")");
    }

    [Theory]
    [InlineData("<rules xmlns=\"" + Nvdl + "\" startMode=\"m\"><mode name=\"m\"><anyNamespace><attach useMode=\"x\"/></anyNamespace></mode></rules>", "mode \"x\" not defined")]
    [InlineData("<rules xmlns=\"" + Nvdl + "\"><namespace ns=\"" + NsA + "\"><allow/></namespace><namespace ns=\"" + NsA + "\"><allow/></namespace></rules>", "rule for elements from namespace \"" + NsA + "\" already specified in this mode")]
    [InlineData("<rules xmlns=\"" + Nvdl + "\"><namespace ns=\"a\"><allow/></namespace></rules>", "namespace URI must be absolute URI")]
    [InlineData("<rules xmlns=\"" + Nvdl + "\"><namespace ns=\"" + NsA + "\"><validate schema=\"a.rng#x\"/></namespace></rules>", "schema URI must not have a fragment identifier")]
    [InlineData("<rules xmlns=\"" + Nvdl + "\"><namespace ns=\"" + NsA + "\"><attach><context path=\"a:b\" useMode=\"m\"/></attach></namespace></rules>", "invalid name")]
    public void CreateSchema_IncorrectSchema_IsReportedWithJingsWording(string nvdl, string message)
    {
        // Arrange
        CollectingErrorHandler errors = new();
        AutoSchemaReader reader = new() { ErrorHandler = errors, Culture = s_english, Resolver = new AnySchemaResolver() };

        // Act & Assert
        FluentActions.Invoking(() => reader.CreateSchema(InputSource.FromString(SchemaUri, nvdl))).Should().Throw<IncorrectSchemaException>();
        errors.Messages.Should().Contain(message);
    }

    [Fact]
    public void CreateSchema_ElementNvdlDoesNotHave_IsReportedByTheSchemaOfNvdl()
    {
        // Arrange
        CollectingErrorHandler errors = new();
        AutoSchemaReader reader = new() { ErrorHandler = errors, Culture = s_english };

        // Act & Assert
        FluentActions.Invoking(() => reader.CreateSchema(InputSource.FromString(SchemaUri, "<rules xmlns=\"" + Nvdl + "\"><foo/></rules>")))
            .Should().Throw<IncorrectSchemaException>();
        errors.Messages.Should().ContainSingle().Which.Should().StartWith("element \"foo\" not allowed");
    }

    [Fact]
    public void CreateSchema_LegacyCompactMediaType_IsAWarning()
    {
        // Arrange
        CollectingErrorHandler errors = new();
        Dictionary<string, string> documents = new() { ["file:///schemas/a.rnc"] = "element foo { empty }" };
        AutoSchemaReader reader = new() { ErrorHandler = errors, Culture = s_english, Resolver = new MapResolver(documents) };

        // Act
        reader.CreateSchema(InputSource.FromString(SchemaUri, Rules("<anyNamespace><validate schema=\"a.rnc\" schemaType=\"application/x-rnc\"/></anyNamespace>")));

        // Assert
        errors.Warnings.ConvertAll(e => e.Message).Should().Equal(
            "the application/x-rnc media type is deprecated, please use application/relax-ng-compact-syntax instead");
    }

    [Theory]
    [InlineData("<schema xmlns=\"urn:x\"/>", "no implementation available for schema language with namespace URI \"urn:x\"")]
    [InlineData("<schema/>", "root element of schema must have a namespace")]
    public void CreateSchema_UnknownLanguage_Throws(string schema, string message)
    {
        // Arrange
        AutoSchemaReader reader = new() { Culture = s_english };

        // Act & Assert
        FluentActions.Invoking(() => reader.CreateSchema(InputSource.FromString(SchemaUri, schema)))
            .Should().Throw<SaxParseException>().WithMessage(message);
    }

    [Fact]
    public void Validate_InPolish_UsesThePolishTexts()
    {
        // Arrange
        ISchema schema = Load(Rules("<namespace ns=\"" + NsA + "\"><allow/></namespace>"));
        CollectingErrorHandler errors = new();

        // Act
        schema.CreateValidator(errors, CultureInfo.GetCultureInfo("pl")).Validate(InputSource.FromString(null, "<foo xmlns=\"" + NsC + "\"/>"), errors);

        // Assert
        errors.Messages.Should().Equal("elementy z przestrzeni nazw \"" + NsC + "\" są niedozwolone");
    }

    [Fact]
    public void NvdlSchemaReader_ReadsAnNvdlSchemaDirectly()
    {
        // Arrange
        NvdlSchemaReader reader = new() { Culture = s_english };

        // Act
        ISchema schema = reader.CreateSchema(InputSource.FromString(SchemaUri, Rules("<anyNamespace><allow/></anyNamespace>")));

        // Assert
        Validate(schema, "<foo xmlns=\"" + NsC + "\"/>").Should().BeEmpty();
    }

    private static string Rules(string content) => "<rules xmlns=\"" + Nvdl + "\">" + content + "</rules>";

    private static ISchema Load(string nvdl, Dictionary<string, string>? documents = null) =>
        new AutoSchemaReader { Culture = s_english, Resolver = new MapResolver(documents ?? []) }.CreateSchema(InputSource.FromString(SchemaUri, nvdl));

    private static List<SaxParseException> Validate(ISchema schema, string document)
    {
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors, s_english).Validate(InputSource.FromString("file:///doc.xml", document), errors);
        return errors.Errors;
    }

    // Serves a schema that allows an empty "foo" for any URI.
    private sealed class AnySchemaResolver : ISchemaResolver
    {
        public InputSource Resolve(string href, string? baseUri) =>
            InputSource.FromString(BasicSchemaResolver.ResolveUri(href, baseUri),
                "<element name=\"foo\" xmlns=\"http://relaxng.org/ns/structure/1.0\"><empty/></element>");
    }

    private sealed class MapResolver(Dictionary<string, string> documents) : ISchemaResolver
    {
        public InputSource Resolve(string href, string? baseUri)
        {
            string uri = BasicSchemaResolver.ResolveUri(href, baseUri);
            return new InputSource(uri, () => documents.TryGetValue(uri, out string? text)
                ? new MemoryStream(Encoding.UTF8.GetBytes(text))
                : throw new FileNotFoundException("No resource " + uri));
        }
    }
}
