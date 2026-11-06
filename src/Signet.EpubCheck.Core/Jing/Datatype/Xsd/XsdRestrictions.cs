using Signet.EpubCheck.Core.Jing.Datatype.Xsd.RegularExpressions;

namespace Signet.EpubCheck.Core.Jing.Datatype.Xsd;

// The facets: each wraps the datatype it restricts (Jing's RestrictDatatype and its subclasses).

/// <summary>
/// A datatype restricted by a facet: everything is the base's unless the facet says otherwise. As in Jing, the
/// description in a facet message is made from the lexical space key (so a list's own description is not used), and
/// a restriction is never context dependent.
/// </summary>
internal abstract class RestrictDatatype(XsdDatatypeBase baseType) : XsdDatatypeBase(baseType.Localizer, baseType.WhiteSpaceMode)
{
    protected XsdDatatypeBase BaseType { get; } = baseType;

    public override string LexicalSpaceKey => BaseType.LexicalSpaceKey;

    public override IOrderRelation? OrderRelation => BaseType.OrderRelation;

    public override IMeasure? Measure => BaseType.Measure;

    public override XsdDatatypeBase Primitive => BaseType.Primitive;

    public override IdType IdType => BaseType.IdType;

    public override bool LexicallyAllows(string str) => BaseType.LexicallyAllows(str);

    public override void CheckLexicallyAllows(string str) => BaseType.CheckLexicallyAllows(str);

    public override bool SameValue(object value1, object value2) => BaseType.SameValue(value1, value2);

    public override int ValueHashCode(object value) => BaseType.ValueHashCode(value);

    public override object GetValue(string str, IValidationContext? vc) => BaseType.GetValue(str, vc);
}

/// <summary>A facet on the value: the value is checked after the base made it.</summary>
internal abstract class ValueRestrictDatatype(XsdDatatypeBase baseType) : RestrictDatatype(baseType)
{
    public override object GetValue(string str, IValidationContext? vc)
    {
        object obj = base.GetValue(str, vc);
        CheckRestriction(obj);
        return obj;
    }

    /// <exception cref="DatatypeException">The facet refuses the value.</exception>
    protected abstract void CheckRestriction(object obj);
}

/// <summary><c>minInclusive</c>.</summary>
internal sealed class MinInclusiveRestrictDatatype(XsdDatatypeBase baseType, object limit, string limitString) : ValueRestrictDatatype(baseType)
{
    private readonly IOrderRelation _order = baseType.OrderRelation!;

    protected override void CheckRestriction(object obj)
    {
        if (!_order.IsLessThan(limit, obj) && !SameValue(obj, limit))
        {
            throw new DatatypeException(Localizer.Message("min_inclusive_violation", DescriptionForRestriction, limitString));
        }
    }
}

/// <summary><c>minExclusive</c>.</summary>
internal sealed class MinExclusiveRestrictDatatype(XsdDatatypeBase baseType, object limit, string limitString) : ValueRestrictDatatype(baseType)
{
    private readonly IOrderRelation _order = baseType.OrderRelation!;

    protected override void CheckRestriction(object obj)
    {
        if (!_order.IsLessThan(limit, obj))
        {
            throw new DatatypeException(Localizer.Message("min_exclusive_violation", DescriptionForRestriction, limitString));
        }
    }
}

/// <summary><c>maxInclusive</c>.</summary>
internal sealed class MaxInclusiveRestrictDatatype(XsdDatatypeBase baseType, object limit, string limitString) : ValueRestrictDatatype(baseType)
{
    private readonly IOrderRelation _order = baseType.OrderRelation!;

    protected override void CheckRestriction(object obj)
    {
        if (!_order.IsLessThan(obj, limit) && !SameValue(obj, limit))
        {
            throw new DatatypeException(Localizer.Message("max_inclusive_violation", DescriptionForRestriction, limitString));
        }
    }
}

/// <summary><c>maxExclusive</c>.</summary>
internal sealed class MaxExclusiveRestrictDatatype(XsdDatatypeBase baseType, object limit, string limitString) : ValueRestrictDatatype(baseType)
{
    private readonly IOrderRelation _order = baseType.OrderRelation!;

    protected override void CheckRestriction(object obj)
    {
        if (!_order.IsLessThan(obj, limit))
        {
            throw new DatatypeException(Localizer.Message("max_exclusive_violation", DescriptionForRestriction, limitString));
        }
    }
}

/// <summary><c>length</c>.</summary>
internal sealed class LengthRestrictDatatype(XsdDatatypeBase baseType, int length) : ValueRestrictDatatype(baseType)
{
    private readonly IMeasure _measure = baseType.Measure!;

    protected override void CheckRestriction(object obj)
    {
        int actualLength = _measure.GetLength(obj);
        if (actualLength != length)
        {
            throw new DatatypeException(Localizer.Message("length_violation",
                DescriptionForRestriction, Localizer.Number(length), Localizer.Number(actualLength)));
        }
    }
}

/// <summary><c>minLength</c>.</summary>
internal sealed class MinLengthRestrictDatatype(XsdDatatypeBase baseType, int length) : ValueRestrictDatatype(baseType)
{
    private readonly IMeasure _measure = baseType.Measure!;

    protected override void CheckRestriction(object obj)
    {
        int actualLength = _measure.GetLength(obj);
        if (actualLength < length)
        {
            throw new DatatypeException(Localizer.Message("min_length_violation",
                DescriptionForRestriction, Localizer.Number(length), Localizer.Number(actualLength)));
        }
    }
}

/// <summary><c>maxLength</c>.</summary>
internal sealed class MaxLengthRestrictDatatype(XsdDatatypeBase baseType, int length) : ValueRestrictDatatype(baseType)
{
    private readonly IMeasure _measure = baseType.Measure!;

    protected override void CheckRestriction(object obj)
    {
        int actualLength = _measure.GetLength(obj);
        if (actualLength > length)
        {
            throw new DatatypeException(Localizer.Message("max_length_violation",
                DescriptionForRestriction, Localizer.Number(length), Localizer.Number(actualLength)));
        }
    }
}

/// <summary><c>pattern</c>: a lexical facet.</summary>
internal sealed class PatternRestrictDatatype(XsdDatatypeBase baseType, XsdRegex pattern, string patternString) : RestrictDatatype(baseType)
{
    public override bool LexicallyAllows(string str) => pattern.Matches(str) && base.LexicallyAllows(str);

    public override void CheckLexicallyAllows(string str)
    {
        base.CheckLexicallyAllows(str);
        if (!pattern.Matches(str))
        {
            throw new DatatypeException(Localizer.Message("pattern_violation", DescriptionForRestriction, patternString));
        }
    }
}

/// <summary><c>totalDigits</c>: Jing counts the digits of the number as written, trailing zeros of the fraction included.</summary>
internal sealed class PrecisionRestrictDatatype(XsdDatatypeBase baseType, int precision) : ValueRestrictDatatype(baseType)
{
    protected override void CheckRestriction(object obj)
    {
        int actualPrecision = ((JavaBigDecimal)obj).Precision;
        if (actualPrecision <= precision)
        {
            return;
        }

        string message = precision == 1
            ? Localizer.Message("precision_1_violation", DescriptionForRestriction, Localizer.Number(actualPrecision))
            : Localizer.Message("precision_violation", DescriptionForRestriction, Localizer.Number(precision), Localizer.Number(actualPrecision));
        throw new DatatypeException(message);
    }
}

/// <summary><c>fractionDigits</c>: Jing counts the digits after the point as written.</summary>
internal class ScaleRestrictDatatype(XsdDatatypeBase baseType, int scale) : ValueRestrictDatatype(baseType)
{
    protected override void CheckRestriction(object obj)
    {
        int actualScale = ((JavaBigDecimal)obj).Scale;
        if (actualScale <= scale)
        {
            return;
        }

        string message = scale switch
        {
            0 => Localizer.Message("scale_0_violation"),
            1 => Localizer.Message("scale_1_violation", Localizer.Number(actualScale)),
            _ => Localizer.Message("scale_violation", Localizer.Number(scale), Localizer.Number(actualScale)),
        };
        throw new DatatypeException(message);
    }
}

/// <summary><c>integer</c>: a decimal with no fraction digits and no trailing point.</summary>
internal sealed class IntegerRestrictDatatype(XsdDatatypeBase baseType) : ScaleRestrictDatatype(baseType, 0)
{
    public override string LexicalSpaceKey => "integer";

    public override bool LexicallyAllows(string str) => base.LexicallyAllows(str) && str[^1] != '.';

    public override void CheckLexicallyAllows(string str)
    {
        if (!LexicallyAllows(str))
        {
            throw CreateLexicallyInvalidException();
        }
    }
}
