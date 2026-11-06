using System.Collections.Generic;
using System.Globalization;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Sax;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// The wording of the errors, as Jing's <c>PatternMatcher</c> and <c>SchemaBuilderImpl</c> compose it from their bundle
/// (EPUBCheck's scenarios match these texts in <c>RSC-005</c>).
/// </summary>
public sealed class RelaxNgMessageTests
{
    private const string Ns = "http://relaxng.org/ns/structure/1.0";
    private const string SchemaUri = "file:///schemas/test.rng";

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Validate_ElementBeforeARequiredOne_SaysWhichIsMissing()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", Element("a", "<empty/>") + Element("b", "<empty/>")));

        // Act
        List<string> messages = Validate(schema, "<doc><b/></doc>");

        // Assert
        messages.Should().Equal("element \"b\" not allowed yet; missing required element \"a\"");
    }

    [Fact]
    public void Validate_UnknownElement_SaysWhatWasExpected()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", Element("a", "<empty/>") + Element("b", "<empty/>")));

        // Act
        List<string> messages = Validate(schema, "<doc><c/></doc>");

        // Assert
        messages.Should().Equal(
            "element \"c\" not allowed anywhere; expected element \"a\"",
            "element \"doc\" incomplete; missing required element \"a\"");
    }

    [Fact]
    public void Validate_ElementOutOfContext_SaysItIsNotAllowedHere()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", Element("a", Element("b", "<empty/>"))));

        // Act
        List<string> messages = Validate(schema, "<doc><b/></doc>");

        // Assert
        messages.Should().Equal(
            "element \"b\" not allowed here; expected element \"a\"",
            "element \"doc\" incomplete; missing required element \"a\"");
    }

    [Fact]
    public void Validate_IncompleteInterleave_ListsAllTheMissingElements()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<interleave>" + Element("a", "<empty/>") + Element("b", "<empty/>") + "</interleave>"));

        // Act
        List<string> messages = Validate(schema, "<doc/>");

        // Assert
        messages.Should().Equal("element \"doc\" incomplete; missing required elements \"a\" and \"b\"");
    }

    [Fact]
    public void Validate_MissingAttribute_NamesIt()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<attribute name=\"x\"/>"));

        // Act
        List<string> messages = Validate(schema, "<doc/>");

        // Assert
        messages.Should().Equal("element \"doc\" missing required attribute \"x\"");
    }

    [Fact]
    public void Validate_UnexpectedAttribute_SaysWhichAreExpected()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<attribute name=\"x\"/>"));

        // Act
        List<string> messages = Validate(schema, "<doc y=\"1\" x=\"1\"/>");

        // Assert
        messages.Should().Equal("attribute \"y\" not allowed here; expected attribute \"x\"");
    }

    [Fact]
    public void Validate_AttributeOnAnElementWithoutAttributes_SaysNoneIsAllowed()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<empty/>"));

        // Act
        List<string> messages = Validate(schema, "<doc y=\"1\"/>");

        // Assert
        messages.Should().Equal("found attribute \"y\", but no attributes allowed here");
    }

    [Fact]
    public void Validate_AttributeValueNotInTheChoice_ListsTheValues()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<attribute name=\"x\"><choice><value>b</value><value>a</value></choice></attribute>"));

        // Act
        List<string> messages = Validate(schema, "<doc x=\"c\"/>");

        // Assert
        messages.Should().Equal("value of attribute \"x\" is invalid; must be equal to \"a\" or \"b\"");
    }

    [Fact]
    public void Validate_ListWithAWrongToken_NamesTheToken()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<attribute name=\"x\"><list><oneOrMore><value>a</value></oneOrMore></list></attribute>"));

        // Act
        List<string> messages = Validate(schema, "<doc x=\"a z a\"/>");

        // Assert
        messages.Should().Equal("value of attribute \"x\" is invalid; token \"z\" invalid; must be equal to \"a\"");
    }

    [Fact]
    public void Validate_TextInAnEmptyElement_SaysTheEndTagWasExpected()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<empty/>"));

        // Act
        List<string> messages = Validate(schema, "<doc>hi</doc>");

        // Assert
        messages.Should().Equal("text not allowed here; expected the element end-tag");
    }

    [Fact]
    public void Validate_WrongElementValue_SaysWhatItMustBe()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<value>yes</value>"));

        // Act
        List<string> messages = Validate(schema, "<doc>no</doc>");

        // Assert
        messages.Should().Equal("character content of element \"doc\" invalid; must be equal to \"yes\"");
    }

    [Fact]
    public void Validate_ElementInTheDefaultNamespace_IsNamedWithoutAPrefix()
    {
        // Arrange
        RelaxNgSchema schema = Load(
            "<element name=\"doc\" ns=\"urn:x\" xmlns=\"" + Ns + "\">" + Element("a", "<empty/>") + "</element>");

        // Act
        List<string> messages = Validate(schema, "<doc xmlns=\"urn:x\"><b/></doc>");

        // Assert
        messages.Should().Equal(
            "element \"b\" not allowed anywhere; expected element \"a\"",
            "element \"doc\" incomplete; missing required element \"a\"");
    }

    [Fact]
    public void Validate_ExpectedElementInAnUndeclaredNamespace_DeclaresItAsTheDefault()
    {
        // Arrange
        RelaxNgSchema schema = Load(
            "<element name=\"doc\" xmlns=\"" + Ns + "\"><element name=\"a\" ns=\"urn:y\"><empty/></element></element>");

        // Act
        List<string> messages = Validate(schema, "<doc><b/></doc>");

        // Assert
        messages.Should().Equal(
            "element \"b\" not allowed anywhere; expected element \"a\" (with xmlns=\"urn:y\")",
            "element \"doc\" incomplete; missing required element \"a\" (with xmlns=\"urn:y\")");
    }

    [Fact]
    public void Validate_InPolish_UsesThePolishTexts()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<attribute name=\"x\"/>"));

        // Act
        List<string> messages = Validate(schema, "<doc/>", CultureInfo.GetCultureInfo("pl"));

        // Assert
        messages.Should().Equal("w elemencie \"doc\" brakuje wymaganego atrybutu \"x\"");
    }

    [Fact]
    public void Validate_ErrorPosition_IsWhereTheReaderIs()
    {
        // Arrange
        RelaxNgSchema schema = Load(Element("doc", "<empty/>"));
        CollectingErrorHandler errors = new();

        // Act
        schema.CreateValidator(errors, s_english).Validate(InputSource.FromString("file:///doc.xml", "<doc>\n  <x/>\n</doc>"));

        // Assert
        errors.Errors.Should().ContainSingle();
        errors.Errors[0].SystemId.Should().Be("file:///doc.xml");
        errors.Errors[0].LineNumber.Should().Be(2);
        errors.Errors[0].ColumnNumber.Should().BePositive();
    }

    [Fact]
    public void CreateSchema_ReferenceToAnUndefinedPattern_IsReported()
    {
        // Arrange
        string schema = "<grammar xmlns=\"" + Ns + "\"><start><ref name=\"foo\"/></start></grammar>";

        // Act
        List<string> messages = SchemaErrors(schema);

        // Assert
        messages.Should().Equal("reference to undefined pattern \"foo\"");
    }

    [Fact]
    public void CreateSchema_GrammarWithoutStart_IsReported()
    {
        // Arrange
        string schema = "<grammar xmlns=\"" + Ns + "\"><define name=\"foo\"><empty/></define></grammar>";

        // Act
        List<string> messages = SchemaErrors(schema);

        // Assert
        messages.Should().Equal("missing \"start\" element");
    }

    [Fact]
    public void CreateSchema_UnknownRelaxNgElement_IsReported()
    {
        // Arrange
        string schema = Element("doc", "<foo/>");

        // Act
        List<string> messages = SchemaErrors(schema);

        // Assert
        messages.Should().Equal("found \"foo\" element but expected a pattern", "missing children");
    }

    [Fact]
    public void CreateSchema_DuplicateAttribute_IsReported()
    {
        // Arrange
        string schema = Element("doc", "<attribute name=\"x\"/><attribute name=\"x\"/>");

        // Act
        List<string> messages = SchemaErrors(schema);

        // Assert
        messages.Should().Equal("duplicate attribute \"x\"");
    }

    [Fact]
    public void CreateSchema_UnknownDatatypeLibrary_IsReported()
    {
        // Arrange
        string schema = Element("doc", "<data type=\"string\" datatypeLibrary=\"http://example.com/dt\"/>");

        // Act
        List<string> messages = SchemaErrors(schema);

        // Assert
        messages.Should().Equal("datatype library \"http://example.com/dt\" not recognized");
    }

    [Fact]
    public void CreateSchema_ParameterOfABuiltinDatatype_IsReported()
    {
        // Arrange
        string schema = Element("doc", "<data type=\"string\"><param name=\"length\">1</param></data>");

        // Act
        List<string> messages = SchemaErrors(schema);

        // Assert
        messages.Should().Equal("invalid parameter: builtin datatypes do not have any parameters");
    }

    [Fact]
    public void CreateSchema_SchemaError_HasTheLocationOfTheElement()
    {
        // Arrange
        string schema = "<grammar xmlns=\"" + Ns + "\">\n<start>\n<ref name=\"foo\"/></start></grammar>";
        CollectingErrorHandler errors = new();
        RelaxNgSchemaReader reader = new() { ErrorHandler = errors, Culture = s_english };

        // Act
        FluentActionsLoad(reader, schema);

        // Assert
        errors.Errors.Should().ContainSingle();
        errors.Errors[0].SystemId.Should().Be(SchemaUri);
        errors.Errors[0].LineNumber.Should().Be(3);
    }

    private static string Element(string name, string content) =>
        "<element name=\"" + name + "\" xmlns=\"" + Ns + "\">" + content + "</element>";

    private static RelaxNgSchema Load(string schema) =>
        new RelaxNgSchemaReader { Culture = s_english }.CreateSchema(InputSource.FromString(SchemaUri, schema));

    private static List<string> Validate(RelaxNgSchema schema, string document, CultureInfo? culture = null)
    {
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors, culture ?? s_english).Validate(InputSource.FromString(null, document));
        return errors.Messages;
    }

    private static List<string> SchemaErrors(string schema)
    {
        CollectingErrorHandler errors = new();
        RelaxNgSchemaReader reader = new() { ErrorHandler = errors, Culture = s_english };
        FluentActionsLoad(reader, schema);
        return errors.Messages;
    }

    private static void FluentActionsLoad(RelaxNgSchemaReader reader, string schema) =>
        FluentActions.Invoking(() => reader.CreateSchema(InputSource.FromString(SchemaUri, schema)))
            .Should().Throw<IncorrectSchemaException>();
}
