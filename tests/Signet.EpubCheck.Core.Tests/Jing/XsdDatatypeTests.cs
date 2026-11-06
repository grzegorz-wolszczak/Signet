using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AwesomeAssertions;
using Signet.EpubCheck.Core.Jing;
using Signet.EpubCheck.Core.Jing.Datatype;
using Signet.EpubCheck.Core.Jing.Datatype.Xsd;
using Signet.EpubCheck.Core.Jing.Datatype.Xsd.RegularExpressions;
using Signet.EpubCheck.Core.Jing.Sax;
using Xunit;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>The XML Schema datatypes: the port of Jing's <c>DatatypeLibraryImplTest</c>, and the wording of their messages.</summary>
public sealed class XsdDatatypeTests
{
    private const string RelaxNg = "http://relaxng.org/ns/structure/1.0";

    private static readonly CultureInfo s_english = CultureInfo.GetCultureInfo("en");
    private static readonly IDatatypeLibrary s_library =
        XsdDatatypeLibraryFactory.For(s_english).CreateDatatypeLibrary(WellKnownNamespaces.XmlSchemaDatatypes)!;

    [Fact]
    public void CheckValid_NotAnInteger_SaysAnIntegerIsExpected()
    {
        // Arrange
        IDatatype dt = s_library.CreateDatatype("integer");

        // Act & Assert
        FluentActions.Invoking(() => dt.CheckValid("foo", null!)).Should().Throw<DatatypeException>()
            .Which.Detail.Should().Be("must be an integer");
    }

    [Theory]
    [InlineData("foo", "must be a decimal number")]
    [InlineData("47", "must be a decimal number less than or equal to 42")]
    [InlineData("-30", "must be a decimal number greater than or equal to -17")]
    [InlineData("0.123", "must be a decimal number with at most 2 significant digits after the decimal point (found 3 digits)")]
    [InlineData("10.12", "must be a decimal number with at most 3 significant digits (found 4 digits)")]
    public void CheckValid_DecimalWithFacets_NamesTheFacetThatFails(string literal, string message)
    {
        // Arrange
        IDatatypeBuilder dtb = s_library.CreateDatatypeBuilder("decimal");
        dtb.AddParameter("fractionDigits", "2", null!);
        dtb.AddParameter("totalDigits", "3", null!);
        dtb.AddParameter("maxInclusive", "42", null!);
        dtb.AddParameter("minInclusive", "-17", null!);
        IDatatype dt = dtb.CreateDatatype();

        // Act & Assert
        FluentActions.Invoking(() => dt.CheckValid(literal, null!)).Should().Throw<DatatypeException>().Which.Detail.Should().Be(message);
    }

    [Fact]
    public void CheckValid_LengthInTheThousands_IsWrittenWithAGroupingSeparator()
    {
        // Arrange
        IDatatypeBuilder dtb = s_library.CreateDatatypeBuilder("string");
        dtb.AddParameter("maxLength", "1000", null!);
        IDatatype dt = dtb.CreateDatatype();

        // Act & Assert
        FluentActions.Invoking(() => dt.CheckValid(new string('x', 1001), null!)).Should().Throw<DatatypeException>()
            .Which.Detail.Should().Be("must be a string with length at most 1,000 (actual length was 1,001)");
    }

    [Theory]
    [InlineData("length", "1.0", "\"length\" parameter must be non negative integer")]
    [InlineData("length", "-1", "\"length\" parameter must be non negative integer")]
    [InlineData("fractionDigits", "1", "\"scale\" parameter can only be applied to datatype derived from \"decimal\"")]
    [InlineData("enumeration", "a", "\"enumeration\" facet is not allowed as a parameter: use \"value\" element instead")]
    [InlineData("foo", "a", "unrecognized parameter \"foo\"")]
    public void AddParameter_WrongParameter_IsRefusedWithJingsMessage(string name, string value, string message)
    {
        // Arrange
        IDatatypeBuilder dtb = s_library.CreateDatatypeBuilder("string");

        // Act & Assert
        FluentActions.Invoking(() => dtb.AddParameter(name, value, null!)).Should().Throw<DatatypeException>().Which.Detail.Should().Be(message);
    }

    [Fact]
    public void AddParameter_LengthOfADate_IsRefused()
    {
        // Arrange
        IDatatypeBuilder dtb = s_library.CreateDatatypeBuilder("date");

        // Act & Assert
        FluentActions.Invoking(() => dtb.AddParameter("length", "10", null!)).Should().Throw<DatatypeException>()
            .Which.Detail.Should().Be("base datatype does not define a unit of length");
    }

    [Fact]
    public void AddParameter_IncorrectPattern_GivesTheTranslatorsMessageAndPosition()
    {
        // Arrange
        IDatatypeBuilder dtb = s_library.CreateDatatypeBuilder("string");

        // Act & Assert
        DatatypeException e = FluentActions.Invoking(() => dtb.AddParameter("pattern", "a{2,1}", null!)).Should().Throw<DatatypeException>().Which;
        e.Detail.Should().Be("invalid regular expression: lower bound of quantifier must not be greater than upper bound");
        e.Index.Should().Be(5);
    }

    [Fact]
    public void CreateDatatypeBuilder_UnknownType_ThrowsWithoutAMessage()
    {
        // Act & Assert
        FluentActions.Invoking(() => s_library.CreateDatatypeBuilder("foo")).Should().Throw<DatatypeException>().Which.Detail.Should().BeNull();
    }

    [Fact]
    public void Validate_InvalidAttributeValue_GivesTheDatatypesReason()
    {
        // Arrange
        RelaxNgSchema schema = Load("<element name=\"doc\" xmlns=\"" + RelaxNg + "\" datatypeLibrary=\"" + WellKnownNamespaces.XmlSchemaDatatypes
            + "\"><attribute name=\"n\"><data type=\"positiveInteger\"/></attribute></element>");

        // Act
        List<string> messages = Validate(schema, "<doc n=\"0\"/>");

        // Assert
        messages.Should().Equal("value of attribute \"n\" is invalid; must be an integer greater than or equal to 1");
    }

    [Fact]
    public void Validate_InPolish_UsesThePolishDatatypeTexts()
    {
        // Arrange
        RelaxNgSchema schema = new RelaxNgSchemaReader { Culture = CultureInfo.GetCultureInfo("pl") }.CreateSchema(InputSource.FromString(null,
            "<element name=\"doc\" xmlns=\"" + RelaxNg + "\" datatypeLibrary=\"" + WellKnownNamespaces.XmlSchemaDatatypes + "\"><data type=\"integer\"/></element>"));

        // Act
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors, CultureInfo.GetCultureInfo("pl")).Validate(InputSource.FromString(null, "<doc>x</doc>"));

        // Assert
        errors.Messages.Should().ContainSingle().Which.Should().EndWith("musi być liczbą całkowitą");
    }

    [Fact]
    public void CreateSchema_IncorrectPatternParameter_ShowsWhereTheErrorIs()
    {
        // Arrange
        CollectingErrorHandler errors = new();
        RelaxNgSchemaReader reader = new() { ErrorHandler = errors, Culture = s_english };
        string schema = "<element name=\"doc\" xmlns=\"" + RelaxNg + "\" datatypeLibrary=\"" + WellKnownNamespaces.XmlSchemaDatatypes
            + "\"><data type=\"string\"><param name=\"pattern\">ab[c</param></data></element>";

        // Act & Assert
        FluentActions.Invoking(() => reader.CreateSchema(InputSource.FromString(null, schema))).Should().Throw<IncorrectSchemaException>();
        errors.Messages.Should().ContainSingle().Which.Should().StartWith("invalid parameter: invalid regular expression: expected \"]\"");
    }

    [Fact]
    public void BlockNames_AllOfJings_AreKnown()
    {
        // Act
        string[] unknown = [.. XsdCharClasses.BlockNames.Where(name => XsdCharClasses.Block(name) is null || XsdCharClasses.Block(name)!.IsEmpty)];

        // Assert
        unknown.Should().BeEmpty();
    }

    [Theory]
    [InlineData("\\p{Lu}+", "ÀB\U0001D400", "aB")]
    [InlineData("[\\i-[:]][\\c-[:]]*", "dc", ":x")]
    [InlineData("\U0001D400{2}", "\U0001D400\U0001D400", "\U0001D400")]
    [InlineData(".", "\U0001D400", "\n")]
    [InlineData("\\w", "a", " ")]
    public void Matches_ClassesAndCharactersOutsideTheBmp_AreCodePoints(string expression, string matching, string notMatching)
    {
        // Arrange
        XsdRegex regex = XsdRegex.Compile(expression, JingLocalizer.ForRegex(s_english));

        // Act
        bool[] results = [regex.Matches(matching), regex.Matches(notMatching)];

        // Assert
        results.Should().Equal(true, false);
    }

    private static RelaxNgSchema Load(string schema) => new RelaxNgSchemaReader { Culture = s_english }.CreateSchema(InputSource.FromString(null, schema));

    private static List<string> Validate(RelaxNgSchema schema, string document)
    {
        CollectingErrorHandler errors = new();
        schema.CreateValidator(errors, s_english).Validate(InputSource.FromString(null, document));
        return errors.Messages;
    }
}
