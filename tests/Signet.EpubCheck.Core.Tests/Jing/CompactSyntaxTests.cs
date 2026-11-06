using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// The compact syntax parser: its messages and where it puts them (Jing counts a tab to the next multiple of 8, a
/// surrogate pair as one column, and puts the end of the input after its last character), escapes, and schemas in the
/// compact syntax used for validation.
/// </summary>
public sealed class CompactSyntaxTests
{
    private const string SchemaUri = "file:///schemas/test.rnc";

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");

    [Theory]
    [InlineData("element foo { empty empty }", 1, 21)]
    [InlineData("element foo {\n\t\tempty empty }", 2, 23)]
    [InlineData("element foo {\r\n  empty empty }", 2, 9)]
    [InlineData("element foo { \"\U0001F600\" empty }", 1, 19)]
    [InlineData("element foo {", 1, 13)]
    [InlineData("element foo { empty ## doc\n}", 1, 21)]
    public void CreateSchema_SyntaxError_IsReportedAtTheToken(string schema, int line, int column)
    {
        // Act
        List<SaxParseException> errors = SchemaErrors(schema);

        // Assert
        errors.Select(e => (e.Message, e.LineNumber, e.ColumnNumber)).Should().Equal(("syntax error", line, column));
        errors[0].SystemId.Should().Be(SchemaUri);
    }

    [Theory]
    [InlineData("element x:foo { empty }", "undeclared prefix \"x\"", 1, 9)]
    [InlineData("element \\x{zz} { empty }", "expected hex digit", 1, 12)]
    [InlineData("element \\x{110000} { empty }", "character code must be less than 0x110000", 1, 17)]
    [InlineData("element \\x{1} { empty }", "reference to character whose code is not allowed", 1, 13)]
    [InlineData("element \\x{41", "incomplete escape sequence", 1, 13)]
    [InlineData("element foo { empty } >> x [ ]", "top-level pattern cannot have following annotations", 1, 23)]
    [InlineData("namespace xmlns = \"urn:x\"\nelement foo { empty }", "prefix must not be \"xmlns\"", 1, 11)]
    public void CreateSchema_Error_IsReportedWithJingsWordingAndPlace(string schema, string message, int line, int column)
    {
        // Act
        List<SaxParseException> errors = SchemaErrors(schema);

        // Assert
        errors.Select(e => (e.Message, e.LineNumber, e.ColumnNumber)).Should().Equal((message, line, column));
    }

    [Fact]
    public void CreateSchema_ExceptInAChoiceWithoutParentheses_IsReportedForEachOperandAfterIt()
    {
        // Act
        List<SaxParseException> errors = SchemaErrors("element foo { string - \"a\" | empty }");

        // Assert
        errors.Select(e => (e.Message, e.ColumnNumber)).Should().Equal(
            ("parentheses required around \"-\" expression", 22),
            ("parentheses required around \"-\" expression", 22));
    }

    [Fact]
    public void CreateSchema_ErrorOfThePatternModule_HasTheLocationOfTheToken()
    {
        // Act
        List<SaxParseException> errors = SchemaErrors("start = element foo { empty }\nstart = text");

        // Assert
        errors.Select(e => (e.Message, e.LineNumber, e.ColumnNumber)).Should().Equal(
            ("multiple definitions of start without \"combine\" attribute", 2, 1));
    }

    [Fact]
    public void CreateSchema_InPolish_UsesThePolishTexts()
    {
        // Act
        List<SaxParseException> errors = SchemaErrors("element foo {", CultureInfo.GetCultureInfo("pl"));

        // Assert
        errors.Select(e => e.Message).Should().Equal("błąd składni");
    }

    [Fact]
    public void Validate_CompactSchema_GivesTheMessagesOfTheXmlSyntax()
    {
        // Arrange
        RelaxNgSchema schema = Load("element doc { element a { empty }, element b { empty } }");

        // Act
        List<string> messages = Validate(schema, "<doc><b/></doc>");

        // Assert
        messages.Should().Equal("element \"b\" not allowed yet; missing required element \"a\"");
    }

    [Fact]
    public void Validate_EscapedNameAndTripleQuotedValue_AreTheCharactersTheyStandFor()
    {
        // Arrange
        RelaxNgSchema schema = Load("element \\x{66}oo { \"\"\"a\nb\"\"\" }");

        // Act
        List<string> valid = Validate(schema, "<foo>a b</foo>");
        List<string> invalid = Validate(schema, "<foo>ab</foo>");

        // Assert
        valid.Should().BeEmpty();
        invalid.Should().Equal("character content of element \"foo\" invalid; must be equal to \"a b\"");
    }

    [Fact]
    public void Validate_CommentsDocumentationAndAnnotations_AreSkipped()
    {
        // Arrange
        string text = """
            # A comment
            namespace a = "http://relaxng.org/ns/compatibility/annotations/1.0"
            namespace eg = "http://example.com/"
            ## Documentation
            ## on two lines
            [ eg:note [ "kept nowhere" ] ]
            start = element foo { [ a:defaultValue = "x" ] attribute bar { text }? } >> eg:after [ ]
            """;
        RelaxNgSchema schema = Load(text);

        // Act
        List<string> messages = Validate(schema, "<foo bar=\"1\"/>");

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public void CreateSchema_IncludeOfACompactDocument_IsReadInTheCompactSyntax()
    {
        // Arrange
        Dictionary<string, string> documents = new()
        {
            ["file:///schemas/common.rnc"] = "a = element a { empty }\nstart = a",
        };
        RelaxNgSchemaReader reader = new() { Culture = s_english, Syntax = RelaxNgSyntax.Compact, Resolver = new MapResolver(documents) };
        RelaxNgSchema schema = reader.CreateSchema(InputSource.FromString(SchemaUri, "include \"common.rnc\" { start = element doc { a } }"));

        // Act
        List<string> messages = Validate(schema, "<doc><b/></doc>");

        // Assert
        messages.Should().Equal(
            "element \"b\" not allowed anywhere; expected element \"a\"",
            "element \"doc\" incomplete; missing required element \"a\"");
    }

    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void CreateSchema_Utf16WithAByteOrderMark_IsRead(string encodingName)
    {
        // Arrange
        Encoding encoding = Encoding.GetEncoding(encodingName);
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes("element foo { empty }")];
        RelaxNgSchemaReader reader = new() { Culture = s_english, Syntax = RelaxNgSyntax.Compact };

        // Act
        RelaxNgSchema schema = reader.CreateSchema(new InputSource(SchemaUri, () => new MemoryStream(bytes)));

        // Assert
        Validate(schema, "<foo/>").Should().BeEmpty();
    }

    private static RelaxNgSchema Load(string schema) =>
        new RelaxNgSchemaReader { Culture = s_english, Syntax = RelaxNgSyntax.Compact }.CreateSchema(InputSource.FromString(SchemaUri, schema));

    private static List<string> Validate(RelaxNgSchema schema, string document)
    {
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors, s_english).Validate(InputSource.FromString(null, document));
        return errors.Messages;
    }

    private static List<SaxParseException> SchemaErrors(string schema, CultureInfo? culture = null)
    {
        CollectingErrorHandler errors = new();
        RelaxNgSchemaReader reader = new() { ErrorHandler = errors, Culture = culture ?? s_english, Syntax = RelaxNgSyntax.Compact };
        FluentActions.Invoking(() => reader.CreateSchema(InputSource.FromString(SchemaUri, schema)))
            .Should().Throw<IncorrectSchemaException>();
        return errors.Errors;
    }

    private sealed class MapResolver(Dictionary<string, string> documents) : ISchemaResolver
    {
        public InputSource Resolve(string href, string? baseUri)
        {
            string uri = BasicSchemaResolver.ResolveUri(href, baseUri);
            return InputSource.FromString(uri, documents[uri]);
        }
    }
}
