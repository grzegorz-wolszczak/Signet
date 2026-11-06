using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using Signet.EpubCheck.Core.Jing.Datatype.Xsd.RegularExpressions;

namespace Signet.EpubCheck.Core.Jing.Datatype.Xsd;

/// <summary>
/// The XML Schema datatype library (Jing's <c>DatatypeLibraryFactoryImpl</c>): the library of
/// <c>http://www.w3.org/2001/XMLSchema-datatypes</c>, with its messages in a language.
/// </summary>
public sealed class XsdDatatypeLibraryFactory : IDatatypeLibraryFactory
{
    private static readonly ConcurrentDictionary<string, XsdDatatypeLibraryFactory> s_instances = new(StringComparer.Ordinal);

    private readonly Lazy<XsdDatatypeLibrary> _library;

    private XsdDatatypeLibraryFactory(CultureInfo? culture)
    {
        _library = new Lazy<XsdDatatypeLibrary>(() => new XsdDatatypeLibrary(JingLocalizer.ForXsdDatatypes(culture), JingLocalizer.ForRegex(culture)));
    }

    /// <summary>The factory whose messages are in the language of <paramref name="culture"/> (the current UI language when <c>null</c>).</summary>
    public static XsdDatatypeLibraryFactory For(CultureInfo? culture) =>
        s_instances.GetOrAdd(culture?.Name ?? CultureInfo.CurrentUICulture.Name, _ => new XsdDatatypeLibraryFactory(culture ?? CultureInfo.CurrentUICulture));

    /// <inheritdoc />
    public IDatatypeLibrary? CreateDatatypeLibrary(string namespaceUri) =>
        string.Equals(namespaceUri, WellKnownNamespaces.XmlSchemaDatatypes, StringComparison.Ordinal) ? _library.Value : null;
}

/// <summary>The datatypes of XML Schema Part 2 by name (Jing's <c>DatatypeLibraryImpl</c>).</summary>
internal sealed class XsdDatatypeLibrary : IDatatypeLibrary
{
    private const string LongMax = "9223372036854775807";
    private const string LongMin = "-9223372036854775808";
    private const string IntMax = "2147483647";
    private const string IntMin = "-2147483648";
    private const string ShortMax = "32767";
    private const string ShortMin = "-32768";
    private const string ByteMax = "127";
    private const string ByteMin = "-128";
    private const string UnsignedLongMax = "18446744073709551615";
    private const string UnsignedIntMax = "4294967295";
    private const string UnsignedShortMax = "65535";
    private const string UnsignedByteMax = "255";

    // Follows the syntax of RFC 3066.
    private const string LanguagePattern = "[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*";

    private readonly FrozenDictionary<string, XsdDatatypeBase> _typeMap;

    public XsdDatatypeLibrary(JingLocalizer localizer, JingLocalizer regexLocalizer)
    {
        Localizer = localizer;
        RegexLocalizer = regexLocalizer;
        DecimalDatatype decimalType = new(localizer);
        IntegerRestrictDatatype integerType = new(decimalType);
        XsdDatatypeBase nonNegativeIntegerType = RestrictMin(integerType, "0");
        NcNameDatatype ncNameType = new(localizer);
        NmtokenDatatype nmtokenType = new(localizer);
        IdrefDatatype idrefType = new(localizer);
        EntityDatatype entityType = new(localizer);
        _typeMap = new Dictionary<string, XsdDatatypeBase>(StringComparer.Ordinal)
        {
            ["string"] = new TokenDatatype(localizer, WhiteSpace.Preserve),
            ["normalizedString"] = new TokenDatatype(localizer, WhiteSpace.Replace),
            ["token"] = new TokenDatatype(localizer),
            ["boolean"] = new BooleanDatatype(localizer),
            ["untypedAtomic"] = new TokenDatatype(localizer, WhiteSpace.Preserve),
            ["anyAtomicType"] = new TokenDatatype(localizer, WhiteSpace.Preserve),
            ["decimal"] = decimalType,
            ["integer"] = integerType,
            ["nonPositiveInteger"] = RestrictMax(integerType, "0"),
            ["negativeInteger"] = RestrictMax(integerType, "-1"),
            ["long"] = RestrictMax(RestrictMin(integerType, LongMin), LongMax),
            ["int"] = RestrictMax(RestrictMin(integerType, IntMin), IntMax),
            ["short"] = RestrictMax(RestrictMin(integerType, ShortMin), ShortMax),
            ["byte"] = RestrictMax(RestrictMin(integerType, ByteMin), ByteMax),
            ["nonNegativeInteger"] = nonNegativeIntegerType,
            ["unsignedLong"] = RestrictMax(nonNegativeIntegerType, UnsignedLongMax),
            ["unsignedInt"] = RestrictMax(nonNegativeIntegerType, UnsignedIntMax),
            ["unsignedShort"] = RestrictMax(nonNegativeIntegerType, UnsignedShortMax),
            ["unsignedByte"] = RestrictMax(nonNegativeIntegerType, UnsignedByteMax),
            ["positiveInteger"] = RestrictMin(integerType, "1"),
            ["double"] = new DoubleDatatype(localizer),
            ["float"] = new FloatDatatype(localizer),
            ["Name"] = new NameDatatype(localizer),
            ["QName"] = new QNameDatatype(localizer),
            ["NCName"] = ncNameType,
            ["NMTOKEN"] = nmtokenType,
            ["NMTOKENS"] = List(nmtokenType),
            ["ID"] = new IdDatatype(localizer),
            ["IDREF"] = idrefType,
            ["IDREFS"] = List(idrefType),
            ["NOTATION"] = new QNameDatatype(localizer),
            ["base64Binary"] = new Base64BinaryDatatype(localizer),
            ["hexBinary"] = new HexBinaryDatatype(localizer),
            ["anyURI"] = new AnyUriDatatype(localizer),
            ["language"] = new RegexDatatype(localizer, XsdRegex.Compile(LanguagePattern, regexLocalizer), "language"),
            ["dateTime"] = DateTimeDatatype.Create(localizer, regexLocalizer, "Y-M-DTt"),
            ["time"] = DateTimeDatatype.Create(localizer, regexLocalizer, "t"),
            ["date"] = DateTimeDatatype.Create(localizer, regexLocalizer, "Y-M-D"),
            ["gYearMonth"] = DateTimeDatatype.Create(localizer, regexLocalizer, "Y-M"),
            ["gYear"] = DateTimeDatatype.Create(localizer, regexLocalizer, "Y"),
            ["gMonthDay"] = DateTimeDatatype.Create(localizer, regexLocalizer, "--M-D"),
            ["gDay"] = DateTimeDatatype.Create(localizer, regexLocalizer, "---D"),
            ["gMonth"] = DateTimeDatatype.Create(localizer, regexLocalizer, "--M"),
            ["ENTITY"] = entityType,
            ["ENTITIES"] = List(entityType),
            ["duration"] = DurationDatatype.Create(localizer, regexLocalizer),
        }.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public JingLocalizer Localizer { get; }

    public JingLocalizer RegexLocalizer { get; }

    /// <exception cref="DatatypeException">There is no datatype of that name (no message, as in Jing).</exception>
    public IDatatypeBuilder CreateDatatypeBuilder(string baseTypeLocalName) =>
        _typeMap.TryGetValue(baseTypeLocalName, out XsdDatatypeBase? baseType)
            ? new XsdDatatypeBuilder(this, baseType)
            : throw new DatatypeException();

    public IDatatype CreateDatatype(string typeLocalName) => CreateDatatypeBuilder(typeLocalName).CreateDatatype();

    private static MaxInclusiveRestrictDatatype RestrictMax(XsdDatatypeBase baseType, string limit) =>
        new(baseType, baseType.GetValue(limit, null), limit);

    private static MinInclusiveRestrictDatatype RestrictMin(XsdDatatypeBase baseType, string limit) =>
        new(baseType, baseType.GetValue(limit, null), limit);

    private MinLengthRestrictDatatype List(XsdDatatypeBase itemType) => new(new ListDatatype(Localizer, itemType), 1);
}

/// <summary>Adds the facets given as parameters to a datatype (Jing's <c>DatatypeBuilderImpl</c>).</summary>
internal sealed class XsdDatatypeBuilder(XsdDatatypeLibrary library, XsdDatatypeBase baseType) : IDatatypeBuilder
{
    private XsdDatatypeBase _base = baseType;

    private JingLocalizer Localizer => library.Localizer;

    public void AddParameter(string name, string value, IValidationContext context)
    {
        switch (name)
        {
            case "pattern":
                AddPatternParam(value);
                break;
            case "minInclusive":
                _base = new MinInclusiveRestrictDatatype(_base, GetLimit(value, context), value);
                break;
            case "maxInclusive":
                _base = new MaxInclusiveRestrictDatatype(_base, GetLimit(value, context), value);
                break;
            case "minExclusive":
                _base = new MinExclusiveRestrictDatatype(_base, GetLimit(value, context), value);
                break;
            case "maxExclusive":
                _base = new MaxExclusiveRestrictDatatype(_base, GetLimit(value, context), value);
                break;
            case "length":
                _base = new LengthRestrictDatatype(_base, GetLength(value));
                break;
            case "minLength":
                _base = new MinLengthRestrictDatatype(_base, GetLength(value));
                break;
            case "maxLength":
                _base = new MaxLengthRestrictDatatype(_base, GetLength(value));
                break;
            case "fractionDigits":
                AddScaleParam(value);
                break;
            case "totalDigits":
                AddPrecisionParam(value);
                break;
            case "enumeration":
                throw Error("enumeration_param");
            case "whiteSpace":
                throw Error("whiteSpace_param");
            default:
                throw Error("unrecognized_param", name);
        }
    }

    public IDatatype CreateDatatype() => _base;

    private void AddPatternParam(string value)
    {
        try
        {
            _base = new PatternRestrictDatatype(_base, XsdRegex.Compile(value, library.RegexLocalizer), value);
        }
        catch (RegexSyntaxException e)
        {
            int pos = e.Position == RegexSyntaxException.UnknownPosition ? DatatypeException.Unknown : e.Position;
            throw new DatatypeException(pos, Localizer.Message("invalid_regex", e.Message));
        }
    }

    private object GetLimit(string str, IValidationContext context)
    {
        if (_base.OrderRelation is null)
        {
            throw Error("not_ordered");
        }

        str = _base.NormalizeWhiteSpace(str);
        try
        {
            _base.CheckLexicallyAllows(str);
            return _base.GetValue(str, context);
        }
        catch (DatatypeException e)
        {
            throw new DatatypeException(Localizer.Message("invalid_limit", str, e.Detail));
        }
    }

    private int GetLength(string str)
    {
        if (_base.Measure is null)
        {
            throw Error("no_length");
        }

        int len = ConvertNonNegativeInteger(str);
        return len >= 0 ? len : throw Error("length_not_non_negative_integer");
    }

    private void AddScaleParam(string str)
    {
        if (_base.Primitive is not DecimalDatatype)
        {
            throw Error("scale_not_derived_from_decimal");
        }

        int scale = ConvertNonNegativeInteger(str);
        _base = scale >= 0 ? new ScaleRestrictDatatype(_base, scale) : throw Error("scale_not_non_negative_integer");
    }

    private void AddPrecisionParam(string str)
    {
        if (_base.Primitive is not DecimalDatatype)
        {
            throw Error("precision_not_derived_from_decimal");
        }

        int precision = ConvertNonNegativeInteger(str);
        _base = precision > 0 ? new PrecisionRestrictDatatype(_base, precision) : throw Error("precision_not_positive_integer");
    }

    private DatatypeException Error(string key, params string?[] args) => new(Localizer.Message(key, args));

    // Java's String.trim removes every character up to U+0020.
    private static readonly char[] s_javaTrimmedChars = MakeJavaTrimmedChars();

    // -1 for anything that is not a non-negative integer, int.MaxValue for one too big. Jing tests the text of the
    // canonical BigDecimal for a point and a minus; Java writes a very small fraction as "1E-7", which has neither, so
    // Jing takes such a value as too big: the port refuses any fraction.
    private static int ConvertNonNegativeInteger(string str)
    {
        str = str.Trim(s_javaTrimmedChars);
        if (!DecimalDatatype.IsDecimalLiteral(str))
        {
            return -1;
        }

        JavaBigDecimal value = DecimalDatatype.ParseValue(str);
        if (value.Sign < 0 || value.Scale > 0)
        {
            return -1;
        }

        return value.Unscaled <= int.MaxValue ? (int)value.Unscaled : int.MaxValue;
    }

    private static char[] MakeJavaTrimmedChars()
    {
        const int LastTrimmed = ' ';
        char[] chars = new char[LastTrimmed + 1];
        for (int i = 0; i <= LastTrimmed; i++)
        {
            chars[i] = (char)i;
        }

        return chars;
    }
}
