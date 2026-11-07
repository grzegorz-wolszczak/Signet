using System;
using System.Collections.Generic;
using System.Globalization;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate;
using Signet.EpubCheck.Core.Jing.Validate.Schematron.XPath;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// Schematron as Jing runs it: the messages its <c>OutputHandler</c> writes, the order of the rules, variables, phases,
/// abstract rules and the checks of its stylesheet; and the XPath 2.0 semantics in XPath 1.0 compatibility mode.
/// </summary>
public sealed class SchematronTests
{
    private const string Sch = "http://purl.oclc.org/dsdl/schematron";
    private const string SchemaUri = "file:///schemas/test.sch";

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");
    private static readonly string s_nl = Environment.NewLine;

    [Fact]
    public void Validate_FailedAssertAndTrueReport_AreWrittenAsJingWritesThem()
    {
        // Arrange
        ISchema schema = Load(Schema("<pattern><rule context=\"a\"><assert test=\"b\">No   <name/> \n child  b.</assert>"
            + "<report test=\"@x\">Attribute x is <value-of select=\"@x\"/>.</report></rule></pattern>"));

        // Act
        List<SaxParseException> errors = Validate(schema, "<doc>\n<a x=\"1\"/></doc>");

        // Assert
        errors.ConvertAll(e => e.Message).Should().Equal(
            "assertion failed:" + s_nl + "  No a child b.",
            "report:" + s_nl + "  Attribute x is 1.");
        errors[0].LineNumber.Should().Be(2);
        errors[0].SystemId.Should().Be("file:///doc.xml");
    }

    [Fact]
    public void Validate_AssertWithoutText_HasNoStatement()
    {
        // Arrange
        ISchema schema = Load(Schema("<pattern><rule context=\"a\"><assert test=\"b\">  </assert></rule></pattern>"));

        // Act
        List<SaxParseException> errors = Validate(schema, "<a/>");

        // Assert
        errors.ConvertAll(e => e.Message).Should().Equal("assertion failed:");
    }

    [Fact]
    public void Validate_OnlyTheFirstMatchingRuleOfAPatternFires()
    {
        // Arrange
        ISchema schema = Load(Schema(
            "<pattern><rule context=\"a[@x]\"><report test=\"true()\">first</report></rule><rule context=\"a\"><report test=\"true()\">second</report></rule></pattern>"
            + "<pattern><rule context=\"a\"><report test=\"true()\">other pattern</report></rule></pattern>"));

        // Act
        List<SaxParseException> errors = Validate(schema, "<doc><a x=\"1\"/><a/></doc>");

        // Assert
        errors.ConvertAll(e => e.Message[(e.Message.IndexOf(' ', StringComparison.Ordinal) + 1)..].Trim()).Should().Equal(
            "first", "other pattern", "second", "other pattern");
    }

    [Fact]
    public void Validate_LetsAndNamespaces_AreInScope()
    {
        // Arrange
        ISchema schema = Load(Schema("<ns prefix=\"h\" uri=\"urn:h\"/><let name=\"all\" value=\"//h:p\"/>"
            + "<pattern><rule context=\"h:p\"><let name=\"id\" value=\"@id\"/>"
            + "<assert test=\"count($all[@id = $id]) = 1\">Duplicate ID \"<value-of select=\"$id\"/>\"</assert></rule></pattern>"));

        // Act
        List<SaxParseException> errors = Validate(schema, "<doc xmlns=\"urn:h\"><p id=\"a\"/><p id=\"a\"/><p id=\"b\"/></doc>");

        // Assert
        errors.ConvertAll(e => e.Message).Should().Equal(
            "assertion failed:" + s_nl + "  Duplicate ID \"a\"",
            "assertion failed:" + s_nl + "  Duplicate ID \"a\"");
    }

    [Fact]
    public void Validate_DefaultPhase_RunsOnlyItsActivePatterns()
    {
        // Arrange
        ISchema schema = Load("<schema xmlns=\"" + Sch + "\" defaultPhase=\"p\"><phase id=\"p\"><active pattern=\"two\"/></phase>"
            + "<pattern id=\"one\"><rule context=\"a\"><report test=\"true()\">one</report></rule></pattern>"
            + "<pattern id=\"two\"><rule context=\"a\"><report test=\"true()\">two</report></rule></pattern></schema>");

        // Act
        List<SaxParseException> errors = Validate(schema, "<a/>");

        // Assert
        errors.ConvertAll(e => e.Message).Should().Equal("report:" + s_nl + "  two");
    }

    [Fact]
    public void Validate_ExtendedAbstractRule_RunsInTheContextOfTheRule()
    {
        // Arrange
        ISchema schema = Load(Schema("<pattern><rule abstract=\"true\" id=\"needs-b\"><assert test=\"b\"><name/> needs b</assert></rule>"
            + "<rule context=\"a\"><extends rule=\"needs-b\"/></rule></pattern>"));

        // Act
        List<SaxParseException> errors = Validate(schema, "<a/>");

        // Assert
        errors.ConvertAll(e => e.Message).Should().Equal("assertion failed:" + s_nl + "  a needs b");
    }

    [Theory]
    [InlineData("<pattern><rule context=\"a\"><extends rule=\"x\"/></rule></pattern>", "cannot extend non-existent rule \"x\"")]
    [InlineData("<pattern><rule context=\"a\" id=\"c\"><assert test=\"1\"/></rule><rule context=\"b\"><extends rule=\"c\"/></rule></pattern>", "cannot extend rule \"c\" because it is not abstract")]
    [InlineData("<pattern><rule context=\"a\"><assert test=\"b[\"/></rule></pattern>", "invalid XPath expression \"b[\": unexpected \"\"")]
    public void CreateSchema_IncorrectSchema_IsReported(string content, string message)
    {
        // Arrange
        CollectingErrorHandler errors = new();
        AutoSchemaReader reader = new() { ErrorHandler = errors, Culture = s_english };

        // Act & Assert
        FluentActions.Invoking(() => reader.CreateSchema(InputSource.FromString(SchemaUri, Schema(content)))).Should().Throw<IncorrectSchemaException>();
        errors.Messages.Should().Contain(message);
    }

    [Fact]
    public void CreateSchema_ElementSchematronDoesNotHave_IsReportedByTheSchemaOfSchematron()
    {
        // Arrange
        CollectingErrorHandler errors = new();
        AutoSchemaReader reader = new() { ErrorHandler = errors, Culture = s_english };

        // Act & Assert
        FluentActions.Invoking(() => reader.CreateSchema(InputSource.FromString(SchemaUri, "<schema xmlns=\"" + Sch + "\"><foo/></schema>")))
            .Should().Throw<IncorrectSchemaException>();
        errors.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_InPolish_UsesThePolishTexts()
    {
        // Arrange
        ISchema schema = Load(Schema("<pattern><rule context=\"a\"><assert test=\"b\">x</assert></rule></pattern>"));
        CollectingErrorHandler errors = new();

        // Act
        schema.CreateValidator(errors, CultureInfo.GetCultureInfo("pl")).Validate(InputSource.FromString(null, "<a/>"), errors);

        // Assert
        errors.Messages.Should().Equal("asercja niespełniona:" + s_nl + "  x");
    }

    [Theory]
    [InlineData("count(//p) = 3", true)]
    [InlineData("//p/@n = 2", true)]
    [InlineData("//p/@n < '10'", true)]
    [InlineData("normalize-space(//p)", true)]
    [InlineData("string-length(normalize-space(//p[2])) = 0", true)]
    [InlineData("tokenize(' a  b', '\\s+') = ''", true)]
    [InlineData("count(tokenize(' a  b', '\\s+'))", true)]
    [InlineData("every $p in //p satisfies $p/@n", true)]
    [InlineData("some $p in //p satisfies $p/@n = 5", false)]
    [InlineData("exists(//p except //p[@n])", false)]
    [InlineData("//p[last()]/@n = 3", true)]
    [InlineData("(//p)[1]/following-sibling::p[1]/@n = 2", true)]
    [InlineData("//p[3]/preceding-sibling::*[1]/self::r", true)]
    [InlineData("//p[3]/preceding-sibling::p[1]/@n = 2", true)]
    [InlineData("//q/ancestor::*[1] is //r", true)]
    [InlineData("if (count(//p) > 2) then true() else false()", true)]
    [InlineData("lower-case('ABC') = ('x', 'abc')", true)]
    [InlineData("matches('Text/HTML; charset=UTF-8', 'text/html;\\s*charset=utf-8', 'i')", true)]
    [InlineData("substring('h3', 2) = '3'", true)]
    [InlineData("number(substring('h3', 2)) + 1 = 4", true)]
    [InlineData("string-join(//p/@n, ',') = '1,2,3'", true)]
    [InlineData("name((//p)[1]) = 'p'", true)]
    [InlineData("distinct-values(('a', 'a', 'b')) = 'b'", true)]
    [InlineData("count(distinct-values(('a', 'a', 'b')))", true)]
    [InlineData("resolve-uri('#x') = 'file:///schemas/test.sch#x'", true)]
    public void XPath_Expression_HasTheValueOfXPath2InCompatibilityMode(string expression, bool expected)
    {
        // Arrange
        XdmTreeBuilder builder = new();
        new XmlEventReader { ContentHandler = builder }.Parse(InputSource.FromString(null, "<doc><p n=\"1\">a</p><p n=\"2\"> </p><r><q/></r><p n=\"3\"/></doc>"));
        XdmDocument doc = builder.Document!;
        XPathContext context = new() { Item = doc, Current = doc, Variables = new NoVariables(), BaseUri = SchemaUri };

        // Act
        bool value = XPathValues.EffectiveBoolean(XPathParser.Parse(expression, new Dictionary<string, string>()).Evaluate(context));

        // Assert
        value.Should().Be(expected, expression);
    }

    // A document with many elements, so that the lists the predicates run over are long enough to be indexed (and applied twice).
    private static string ManyItems(int count, Func<int, string> element)
    {
        System.Text.StringBuilder doc = new("<doc>");
        for (int i = 0; i < count; i++)
        {
            doc.Append(element(i));
        }

        return doc.Append("</doc>").ToString();
    }

    [Fact]
    public void Validate_AnEqualityPredicateOnAKeptList_FindsTheSameNodesAsLookingAtEveryOne()
    {
        // Arrange: ids 0..39, the ids 7 and 21 twice; the variable's list is applied to once for every element.
        ISchema schema = Load(Schema("<let name=\"all\" value=\"//*[@id]\"/><pattern><rule context=\"*[@id]\">"
            + "<assert test=\"count($all[@id = current()/@id]) = 1\">Duplicate ID \"<value-of select=\"@id\"/>\"</assert></rule></pattern>"));
        string document = ManyItems(40, i => "<a id=\"" + (i == 21 ? 7 : i).ToString(CultureInfo.InvariantCulture) + "\"/>");

        // Act
        List<SaxParseException> errors = Validate(schema, document);

        // Assert
        errors.ConvertAll(e => e.Message.Replace(s_nl, " ")).Should().Equal(
            "assertion failed:   Duplicate ID \"7\"", "assertion failed:   Duplicate ID \"7\"");
    }

    [Fact]
    public void Validate_APredicateOnAllTheNodesOfTheDocument_IsAppliedToThePathFromTheRoot()
    {
        // Arrange: //@id[normalize-space(.) = normalize-space(current()/@id)] counts the ids equal to this one, white space aside.
        ISchema schema = Load(Schema("<pattern><rule context=\"*[@id]\">"
            + "<assert test=\"count(//@id[normalize-space(.) = normalize-space(current()/@id)]) = 1\">Duplicate ID \"<value-of select=\"@id\"/>\"</assert></rule></pattern>"));
        string document = ManyItems(30, i => "<a id=\"" + (i == 3 ? " 11 " : i.ToString(CultureInfo.InvariantCulture)) + "\"/>");

        // Act
        List<SaxParseException> errors = Validate(schema, document);

        // Assert
        errors.ConvertAll(e => e.Message.Replace(s_nl, " ")).Should().Equal(
            "assertion failed:   Duplicate ID \" 11 \"", "assertion failed:   Duplicate ID \"11\"");
    }

    [Fact]
    public void Validate_APredicateThatComparesTheItemWithTheCurrentNode_IsNotConfusedByTheIndexOfAnotherNode()
    {
        // Arrange: each element must be followed by an element whose id is its own id plus one, through a left side that uses
        // current() as well as the item: a table made for the first current node would be wrong for the others.
        ISchema schema = Load(Schema("<let name=\"all\" value=\"//*[@n]\"/><pattern><rule context=\"*[@n]\">"
            + "<assert test=\"count($all[number(@n) - number(current()/@n) = 1]) = 1 or @n = '39'\">no successor of <value-of select=\"@n\"/></assert></rule></pattern>"));
        string document = ManyItems(40, i => "<a n=\"" + i.ToString(CultureInfo.InvariantCulture) + "\"/>");

        // Act
        List<SaxParseException> errors = Validate(schema, document);

        // Assert
        errors.Should().BeEmpty();
    }

    private static string Schema(string content) => "<schema xmlns=\"" + Sch + "\">" + content + "</schema>";

    private static ISchema Load(string schema) =>
        new AutoSchemaReader { Culture = s_english }.CreateSchema(InputSource.FromString(SchemaUri, schema));

    private static List<SaxParseException> Validate(ISchema schema, string document)
    {
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors, s_english).Validate(InputSource.FromString("file:///doc.xml", document), errors);
        return errors.Errors;
    }

    private sealed class NoVariables : IXPathVariables
    {
        public IReadOnlyList<object> Get(string name) => throw new XPathException("no variable " + name);
    }
}
