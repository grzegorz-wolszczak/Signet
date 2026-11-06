using System;
using System.Globalization;
using System.Linq;
using Signet.EpubCheck.Core.Jing.Datatype.Xsd.RegularExpressions;

namespace Signet.EpubCheck.Core.Jing.Datatype.Xsd;

// The primitive and derived datatypes of Jing's xsd-datatype module, one class each as in Jing.

/// <summary><c>token</c> (and, with another white space mode, <c>string</c> and <c>normalizedString</c>).</summary>
internal class TokenDatatype(JingLocalizer localizer, WhiteSpace whiteSpace = WhiteSpace.Collapse)
    : XsdDatatypeBase(localizer, whiteSpace), IMeasure
{
    public override string LexicalSpaceKey => "string";

    public override bool AlwaysValid => true;

    public override IMeasure? Measure => this;

    public override bool LexicallyAllows(string str) => true;

    public override object GetValue(string str, IValidationContext? vc) => str;

    // A length in characters: a surrogate pair is one.
    public virtual int GetLength(object obj)
    {
        string str = (string)obj;
        return str.Length - str.Count(char.IsHighSurrogate);
    }
}

/// <summary><c>boolean</c>.</summary>
internal sealed class BooleanDatatype(JingLocalizer localizer) : XsdDatatypeBase(localizer)
{
    public override string LexicalSpaceKey => "boolean";

    public override bool LexicallyAllows(string str) => str is "true" or "false" or "1" or "0";

    public override object GetValue(string str, IValidationContext? vc) => str[0] is 't' or '1';
}

/// <summary><c>decimal</c>, the primitive of the integer types.</summary>
internal sealed class DecimalDatatype(JingLocalizer localizer) : XsdDatatypeBase(localizer), IOrderRelation
{
    public override string LexicalSpaceKey => "decimal";

    public override IOrderRelation? OrderRelation => this;

    public static bool IsDecimalLiteral(string str)
    {
        int len = str.Length;
        if (len == 0)
        {
            return false;
        }

        int i = 0;
        if (str[i] is '+' or '-' && ++i == len)
        {
            return false;
        }

        bool hadDecimalPoint = false;
        if (str[i] == '.')
        {
            hadDecimalPoint = true;
            if (++i == len)
            {
                return false;
            }
        }

        do
        {
            char c = str[i];
            if (c == '.')
            {
                if (hadDecimalPoint)
                {
                    return false;
                }

                hadDecimalPoint = true;
            }
            else if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }
        while (++i < len);
        return true;
    }

    public static JavaBigDecimal ParseValue(string str) => JavaBigDecimal.Parse(str[0] == '+' ? str[1..] : str);

    public override bool LexicallyAllows(string str) => IsDecimalLiteral(str);

    public override object GetValue(string str, IValidationContext? vc) => ParseValue(str);

    public bool IsLessThan(object obj1, object obj2) => ((JavaBigDecimal)obj1).CompareTo((JavaBigDecimal)obj2) < 0;

    // Java's BigDecimal.equals also compares the scales: Jing compares the values.
    public override bool SameValue(object value1, object value2) => ((JavaBigDecimal)value1).CompareTo((JavaBigDecimal)value2) == 0;

    public override int ValueHashCode(object value) => ((JavaBigDecimal)value).ToBigInteger().GetHashCode();
}

/// <summary><c>double</c> (and the base of <c>float</c>).</summary>
internal class DoubleDatatype(JingLocalizer localizer) : XsdDatatypeBase(localizer), IOrderRelation
{
    private const string PositiveInfinity = "INF";
    private const string NegativeInfinity = "-INF";
    private const string NotANumber = "NaN";

    public override string LexicalSpaceKey => "float";

    public override IOrderRelation? OrderRelation => this;

    public override bool LexicallyAllows(string str)
    {
        if (str is PositiveInfinity or NegativeInfinity or NotANumber)
        {
            return true;
        }

        bool hadSign = false;
        bool hadDecimalPoint = false;
        bool hadDigit = false;
        bool hadE = false;
        foreach (char c in str)
        {
            switch (c)
            {
                case '+' or '-':
                    if (hadDigit || hadDecimalPoint || hadSign)
                    {
                        return false;
                    }

                    hadSign = true;
                    break;
                case >= '0' and <= '9':
                    hadDigit = true;
                    break;
                case 'e' or 'E':
                    if (hadE || !hadDigit)
                    {
                        return false;
                    }

                    hadDigit = false;
                    hadE = true;
                    hadSign = false;
                    hadDecimalPoint = false;
                    break;
                case '.':
                    if (hadDecimalPoint || hadE)
                    {
                        return false;
                    }

                    hadDecimalPoint = true;
                    break;
                default:
                    return false;
            }
        }

        return hadDigit;
    }

    public override object GetValue(string str, IValidationContext? vc) => str switch
    {
        PositiveInfinity => double.PositiveInfinity,
        NegativeInfinity => double.NegativeInfinity,
        NotANumber => double.NaN,
        _ => double.Parse(str, NumberStyles.Float, CultureInfo.InvariantCulture),
    };

    public virtual bool IsLessThan(object obj1, object obj2) => (double)obj1 < (double)obj2;

    // NaN is the same as NaN, and 0 as -0.
    public override bool SameValue(object value1, object value2)
    {
        double d1 = (double)value1;
        double d2 = (double)value2;
        return d1 == d2 || (double.IsNaN(d1) && double.IsNaN(d2));
    }

    public override int ValueHashCode(object value) => (double)value == 0 ? 0 : value.GetHashCode();
}

/// <summary><c>float</c>.</summary>
internal sealed class FloatDatatype(JingLocalizer localizer) : DoubleDatatype(localizer)
{
    public override object GetValue(string str, IValidationContext? vc) => str switch
    {
        "INF" => float.PositiveInfinity,
        "-INF" => float.NegativeInfinity,
        "NaN" => float.NaN,
        _ => float.Parse(str, NumberStyles.Float, CultureInfo.InvariantCulture),
    };

    public override bool IsLessThan(object obj1, object obj2) => (float)obj1 < (float)obj2;

    public override bool SameValue(object value1, object value2)
    {
        float f1 = (float)value1;
        float f2 = (float)value2;
        return f1 == f2 || (float.IsNaN(f1) && float.IsNaN(f2));
    }

    public override int ValueHashCode(object value) => (float)value == 0 ? 0 : value.GetHashCode();
}

/// <summary><c>Name</c>.</summary>
internal class NameDatatype(JingLocalizer localizer) : TokenDatatype(localizer)
{
    public override string LexicalSpaceKey => "name";

    public override bool AlwaysValid => false;

    public override bool LexicallyAllows(string str) => Naming.IsName(str);

    // A name has no surrogate pairs.
    public override int GetLength(object obj) => ((string)obj).Length;
}

/// <summary><c>NCName</c> (and the base of <c>ID</c>, <c>IDREF</c>, <c>ENTITY</c>).</summary>
internal class NcNameDatatype(JingLocalizer localizer) : NameDatatype(localizer)
{
    public override string LexicalSpaceKey => "ncname";

    public override bool LexicallyAllows(string str) => Naming.IsNcname(str);
}

/// <summary><c>NMTOKEN</c>.</summary>
internal sealed class NmtokenDatatype(JingLocalizer localizer) : TokenDatatype(localizer)
{
    public override string LexicalSpaceKey => "nmtoken";

    public override bool AlwaysValid => false;

    public override bool LexicallyAllows(string str) => Naming.IsNmtoken(str);

    public override int GetLength(object obj) => ((string)obj).Length;
}

/// <summary><c>QName</c> and <c>NOTATION</c>: the value is the expanded name.</summary>
internal sealed class QNameDatatype(JingLocalizer localizer) : XsdDatatypeBase(localizer)
{
    private const char PrefixSeparator = ':';

    public override string LexicalSpaceKey => "qname";

    public override bool IsContextDependent => true;

    public override bool LexicallyAllows(string str) => Naming.IsQname(str);

    public override object GetValue(string str, IValidationContext? vc)
    {
        int i = str.IndexOf(PrefixSeparator, StringComparison.Ordinal);
        if (i < 0)
        {
            return new Name(vc?.ResolveNamespacePrefix(string.Empty) ?? string.Empty, str);
        }

        string prefix = str[..i];
        string ns = vc?.ResolveNamespacePrefix(prefix)
            ?? throw new DatatypeException(Localizer.Message("undeclared_prefix", prefix));
        return new Name(ns, str[(i + 1)..]);
    }

    public override bool AllowsValue(string str, IValidationContext? vc)
    {
        int i = str.IndexOf(PrefixSeparator, StringComparison.Ordinal);
        return i < 0 || vc?.ResolveNamespacePrefix(str[..i]) is not null;
    }
}

/// <summary><c>ID</c>.</summary>
internal sealed class IdDatatype(JingLocalizer localizer) : NcNameDatatype(localizer)
{
    public override IdType IdType => IdType.Id;
}

/// <summary><c>IDREF</c>.</summary>
internal sealed class IdrefDatatype(JingLocalizer localizer) : NcNameDatatype(localizer)
{
    public override IdType IdType => IdType.IdRef;
}

/// <summary><c>ENTITY</c>: the name of an unparsed entity of the DTD.</summary>
internal sealed class EntityDatatype(JingLocalizer localizer) : NcNameDatatype(localizer)
{
    public override bool AllowsValue(string str, IValidationContext? vc) => vc?.IsUnparsedEntity(str) == true;

    public override object GetValue(string str, IValidationContext? vc) =>
        AllowsValue(str, vc) ? str : throw new DatatypeException(Localizer.Message("entity_violation"));
}

/// <summary><c>anyURI</c>: what Jing's <c>Uri.isValid</c> accepts.</summary>
internal sealed class AnyUriDatatype(JingLocalizer localizer) : TokenDatatype(localizer)
{
    public override string LexicalSpaceKey => "uri";

    public override bool AlwaysValid => false;

    public override bool LexicallyAllows(string str) => JingUri.IsValid(str);
}

/// <summary>A datatype whose lexical space is a regular expression (<c>language</c>, the dates, <c>duration</c>).</summary>
internal class RegexDatatype(JingLocalizer localizer, XsdRegex regex, string lexicalSpaceKey = "string") : TokenDatatype(localizer)
{
    public override string LexicalSpaceKey => lexicalSpaceKey;

    public override bool AlwaysValid => false;

    public override bool LexicallyAllows(string str) => regex.Matches(str);
}

/// <summary>A list of items of a datatype (<c>NMTOKENS</c>, <c>IDREFS</c>, <c>ENTITIES</c>).</summary>
internal sealed class ListDatatype(JingLocalizer localizer, XsdDatatypeBase itemType) : XsdDatatypeBase(localizer), IMeasure
{
    private const string ListKey = "list";
    private static readonly char[] s_whiteSpace = [' ', '\t', '\n', '\r'];

    public override string LexicalSpaceKey => ListKey + "_" + itemType.LexicalSpaceKey;

    // For a blank string Jing says "a whitespace-delimited list with length …", not "a list of XML NMTOKENs with length …".
    public override string DescriptionForRestriction => LexicalSpaceDescription(ListKey);

    public override IMeasure? Measure => this;

    public override bool IsContextDependent => itemType.IsContextDependent;

    public override IdType IdType => itemType.IdType == IdType.IdRef ? IdType.IdRefs : IdType.Null;

    public override bool LexicallyAllows(string str) => Split(str).All(itemType.LexicallyAllows);

    public override object GetValue(string str, IValidationContext? vc) => Split(str).Select(t => itemType.GetValue(t, vc)).ToArray();

    public int GetLength(object obj) => ((object[])obj).Length;

    public override int ValueHashCode(object value) => ((object[])value).Aggregate(0, (hc, item) => hc ^ itemType.ValueHashCode(item));

    public override bool SameValue(object value1, object value2)
    {
        object[] items1 = (object[])value1;
        object[] items2 = (object[])value2;
        return items1.Length == items2.Length && items1.Zip(items2).All(p => itemType.SameValue(p.First, p.Second));
    }

    // Jing's StringSplitter: the tokens between XML white space.
    private static string[] Split(string str) => str.Split(s_whiteSpace, StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>The base of the binary datatypes: the value is the bytes, the length their number.</summary>
internal abstract class BinaryDatatype(JingLocalizer localizer) : XsdDatatypeBase(localizer, WhiteSpace.Preserve), IMeasure
{
    // Jing collapses the white space itself, for speed.
    private const int HashMultiplier = 33;
    private const int ByteMask = 0xFF;

    public override IMeasure? Measure => this;

    public override int ValueHashCode(object value) => ((byte[])value).Aggregate(0, (hc, b) => (hc * HashMultiplier) ^ (b & ByteMask));

    public override bool SameValue(object value1, object value2) => ((byte[])value1).AsSpan().SequenceEqual((byte[])value2);

    public int GetLength(object obj) => ((byte[])obj).Length;
}

/// <summary><c>base64Binary</c>.</summary>
internal sealed class Base64BinaryDatatype(JingLocalizer localizer) : BinaryDatatype(localizer)
{
    private const sbyte Invalid = -1;
    private const sbyte WhiteSpaceWeight = -2;
    private const sbyte Padding = -3;
    private const int AsciiLimit = 128;
    private const int QuartetMask = 0x3;
    private const int OnePaddingUnusedBits = 0x3;
    private const int TwoPaddingUnusedBits = 0xF;
    private const int BitsPerChar = 6;
    private const int CharsPerQuartet = 4;
    private const int BytesPerQuartet = 3;
    private const int QuartetShift = 2;
    private const int FirstByteShift = 16;
    private const int BitsPerByte = 8;
    private const int ByteMask = 0xFF;
    private const int MaxPadding = 2;

    private static readonly sbyte[] s_weightTable = MakeWeightTable();

    public override string LexicalSpaceKey => "base64";

    public override bool LexicallyAllows(string str) => ByteCount(str) >= 0;

    public override object GetValue(string str, IValidationContext? vc)
    {
        int nBytes = ByteCount(str);
        byte[] value = new byte[nBytes];
        int valueIndex = 0;
        int nBytesAccum = 0;
        int accum = 0;
        foreach (char c in str)
        {
            int w = s_weightTable[c];
            if (w == WhiteSpaceWeight)
            {
                continue;
            }

            accum <<= BitsPerChar;
            if (w != Padding)
            {
                accum |= w;
            }

            if (++nBytesAccum == CharsPerQuartet)
            {
                for (int shift = FirstByteShift; shift >= 0; shift -= BitsPerByte)
                {
                    if (valueIndex < nBytes)
                    {
                        value[valueIndex++] = (byte)((accum >> shift) & ByteMask);
                    }
                }

                nBytesAccum = 0;
                accum = 0;
            }
        }

        return value;
    }

    private static int ByteCount(string str)
    {
        int nChars = 0;
        int nPadding = 0;
        int lastCharWeight = -1;
        foreach (char c in str)
        {
            if (c >= AsciiLimit)
            {
                return -1;
            }

            int w = s_weightTable[c];
            switch (w)
            {
                case WhiteSpaceWeight:
                    break;
                case Padding:
                    if (++nPadding > MaxPadding)
                    {
                        return -1;
                    }

                    break;
                case Invalid:
                    return -1;
                default:
                    if (nPadding > 0)
                    {
                        return -1;
                    }

                    lastCharWeight = w;
                    nChars++;
                    break;
            }
        }

        if (((nChars + nPadding) & QuartetMask) != 0)
        {
            return -1;
        }

        // One padding character: the last character must leave 2 bits unused; two: 4 bits.
        if ((nPadding == 1 && (lastCharWeight & OnePaddingUnusedBits) != 0)
            || (nPadding == MaxPadding && (lastCharWeight & TwoPaddingUnusedBits) != 0))
        {
            return -1;
        }

        return ((nChars + nPadding) >> QuartetShift) * BytesPerQuartet - nPadding;
    }

    private static sbyte[] MakeWeightTable()
    {
        sbyte[] w = new sbyte[AsciiLimit];
        Array.Fill(w, Invalid);
        sbyte n = 0;
        for (int i = 'A'; i <= 'Z'; i++, n++)
        {
            w[i] = n;
        }

        for (int i = 'a'; i <= 'z'; i++, n++)
        {
            w[i] = n;
        }

        for (int i = '0'; i <= '9'; i++, n++)
        {
            w[i] = n;
        }

        w['+'] = n++;
        w['/'] = n;
        w[' '] = w['\t'] = w['\r'] = w['\n'] = WhiteSpaceWeight;
        w['='] = Padding;
        return w;
    }
}

/// <summary><c>hexBinary</c>.</summary>
internal sealed class HexBinaryDatatype(JingLocalizer localizer) : BinaryDatatype(localizer)
{
    private const int Invalid = -1;
    private const int WhiteSpaceWeight = -2;
    private const int HexLetterBase = 10;
    private const int NibbleShift = 4;

    public override string LexicalSpaceKey => "hex";

    public override bool LexicallyAllows(string str)
    {
        int len = str.Length;
        int i = 0;
        while (i < len && Weight(str[i]) == WhiteSpaceWeight)
        {
            i++;
        }

        while (i + 1 < len && Weight(str[i]) >= 0 && Weight(str[i + 1]) >= 0)
        {
            i += 2;
        }

        while (i < len && Weight(str[i]) == WhiteSpaceWeight)
        {
            i++;
        }

        return i == len;
    }

    public override object GetValue(string str, IValidationContext? vc)
    {
        int start = 0;
        while (start < str.Length && Weight(str[start]) == WhiteSpaceWeight)
        {
            start++;
        }

        int end = str.Length;
        while (end > start && Weight(str[end - 1]) == WhiteSpaceWeight)
        {
            end--;
        }

        byte[] value = new byte[(end - start) >> 1];
        for (int i = start, j = 0; i < end; i += 2, j++)
        {
            value[j] = (byte)((Weight(str[i]) << NibbleShift) | Weight(str[i + 1]));
        }

        return value;
    }

    private static int Weight(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + HexLetterBase,
        >= 'a' and <= 'f' => c - 'a' + HexLetterBase,
        ' ' or '\n' or '\r' or '\t' => WhiteSpaceWeight,
        _ => Invalid,
    };
}
