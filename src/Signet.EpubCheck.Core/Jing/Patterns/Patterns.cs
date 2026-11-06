using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Signet.EpubCheck.Core.Jing.Datatype;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>The content type of a pattern (RELAX NG section 7.2): what its content can be grouped with.</summary>
internal enum ContentType
{
    Empty = 0,
    Element = 1,
    Mixed = 2,
    Data = 3,
}

/// <summary>Where a pattern is, for the restrictions of RELAX NG section 7.1.</summary>
internal enum RestrictionContext
{
    Start = 0,
    Element = 1,
    ElementRepeat = 2,
    ElementRepeatGroup = 3,
    ElementRepeatInterleave = 4,
    Attribute = 5,
    List = 6,
    DataExcept = 7,
}

/// <summary>
/// A pattern of a simplified schema (Jing's <c>Pattern</c>). Patterns are interned by the builders: two equal patterns
/// are the same object, so operands are compared by reference.
/// </summary>
internal abstract class Pattern
{
    internal const int TextHashCode = 1;
    internal const int ErrorHashCode = 3;
    internal const int EmptyHashCode = 5;
    internal const int NotAllowedHashCode = 7;
    internal const int ChoiceHashCode = 11;
    internal const int GroupHashCode = 13;
    internal const int InterleaveHashCode = 17;
    internal const int OneOrMoreHashCode = 19;
    internal const int ElementHashCode = 23;
    internal const int ValueHashCode = 27;
    internal const int AttributeHashCode = 29;
    internal const int DataHashCode = 31;
    internal const int ListHashCode = 37;
    internal const int AfterHashCode = 41;

    protected Pattern(bool nullable, ContentType contentType, int hashCode)
    {
        IsNullable = nullable;
        ContentType = contentType;
        PatternHashCode = hashCode;
    }

    protected Pattern()
    {
        IsNullable = false;
        PatternHashCode = RuntimeHelpers.GetHashCode(this);
        ContentType = ContentType.Empty;
    }

    /// <summary>Whether the pattern matches the empty sequence.</summary>
    public bool IsNullable { get; }

    /// <summary>The hash code of the pattern's structure (the one the interner uses).</summary>
    public int PatternHashCode { get; }

    public ContentType ContentType { get; }

    public virtual bool IsNotAllowed => false;

    /// <summary>The identity hash code Java's <c>Object.hashCode()</c> gives: what the composite patterns combine.</summary>
    internal static int Identity(object o) => RuntimeHelpers.GetHashCode(o);

    internal static int CombineHashCode(int hc1, int hc2, int hc3) => hc1 * hc2 * hc3;

    internal static int CombineHashCode(int hc1, int hc2) => hc1 * hc2;

    internal static bool ContentTypeGroupable(ContentType ct1, ContentType ct2)
    {
        if (ct1 == ContentType.Empty || ct2 == ContentType.Empty)
        {
            return true;
        }

        return ct1 != ContentType.Data && ct2 != ContentType.Data;
    }

    /// <exception cref="SaxParseException">A reference recurses without an element in between.</exception>
    public virtual void CheckRecursion(int depth, JingLocalizer localizer)
    {
    }

    public virtual Pattern Expand(SchemaPatternBuilder b) => this;

    /// <exception cref="RestrictionViolationException">The pattern breaks a restriction of RELAX NG section 7.</exception>
    public virtual void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
    }

    /// <summary>Whether <paramref name="other"/> (not null) has the same structure.</summary>
    public abstract bool SamePattern(Pattern other);

    public virtual bool ContainsChoice(Pattern p) => ReferenceEquals(this, p);

    public abstract T Apply<T>(IPatternFunction<T> f);
}

internal abstract class BinaryPattern : Pattern
{
    protected BinaryPattern(bool nullable, int hashCode, Pattern p1, Pattern p2)
        : base(nullable, p1.ContentType > p2.ContentType ? p1.ContentType : p2.ContentType, hashCode)
    {
        Operand1 = p1;
        Operand2 = p2;
    }

    public Pattern Operand1 { get; }

    public Pattern Operand2 { get; }

    public override void CheckRecursion(int depth, JingLocalizer localizer)
    {
        Operand1.CheckRecursion(depth, localizer);
        Operand2.CheckRecursion(depth, localizer);
    }

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        Operand1.CheckRestrictions(context, dad, alpha);
        Operand2.CheckRestrictions(context, dad, alpha);
    }

    public override bool SamePattern(Pattern other) =>
        GetType() == other.GetType()
        && other is BinaryPattern b
        && ReferenceEquals(Operand1, b.Operand1)
        && ReferenceEquals(Operand2, b.Operand2);
}

internal sealed class ChoicePattern(Pattern p1, Pattern p2)
    : BinaryPattern(p1.IsNullable || p2.IsNullable, CombineHashCode(ChoiceHashCode, Identity(p1), Identity(p2)), p1, p2)
{
    public override Pattern Expand(SchemaPatternBuilder b)
    {
        Pattern ep1 = Operand1.Expand(b);
        Pattern ep2 = Operand2.Expand(b);
        return !ReferenceEquals(ep1, Operand1) || !ReferenceEquals(ep2, Operand2) ? b.MakeChoice(ep1, ep2) : this;
    }

    public override bool ContainsChoice(Pattern p) => Operand1.ContainsChoice(p) || Operand2.ContainsChoice(p);

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseChoice(this);

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        dad?.StartChoice();
        Operand1.CheckRestrictions(context, dad, alpha);
        dad?.Alternative();
        Operand2.CheckRestrictions(context, dad, alpha);
        dad?.EndChoice();
    }
}

internal sealed class GroupPattern(Pattern p1, Pattern p2)
    : BinaryPattern(p1.IsNullable && p2.IsNullable, CombineHashCode(GroupHashCode, Identity(p1), Identity(p2)), p1, p2)
{
    public override Pattern Expand(SchemaPatternBuilder b)
    {
        Pattern ep1 = Operand1.Expand(b);
        Pattern ep2 = Operand2.Expand(b);
        return !ReferenceEquals(ep1, Operand1) || !ReferenceEquals(ep2, Operand2) ? b.MakeGroup(ep1, ep2) : this;
    }

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        switch (context)
        {
            case RestrictionContext.Start:
                throw new RestrictionViolationException("start_contains_group");
            case RestrictionContext.DataExcept:
                throw new RestrictionViolationException("data_except_contains_group");
        }

        base.CheckRestrictions(context == RestrictionContext.ElementRepeat ? RestrictionContext.ElementRepeatGroup : context, dad, alpha);
        if (context != RestrictionContext.List && !ContentTypeGroupable(Operand1.ContentType, Operand2.ContentType))
        {
            throw new RestrictionViolationException("group_string");
        }
    }

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseGroup(this);
}

internal sealed class InterleavePattern(Pattern p1, Pattern p2)
    : BinaryPattern(p1.IsNullable && p2.IsNullable, CombineHashCode(InterleaveHashCode, Identity(p1), Identity(p2)), p1, p2)
{
    public override Pattern Expand(SchemaPatternBuilder b)
    {
        Pattern ep1 = Operand1.Expand(b);
        Pattern ep2 = Operand2.Expand(b);
        return !ReferenceEquals(ep1, Operand1) || !ReferenceEquals(ep2, Operand2) ? b.MakeInterleave(ep1, ep2) : this;
    }

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        switch (context)
        {
            case RestrictionContext.Start:
                throw new RestrictionViolationException("start_contains_interleave");
            case RestrictionContext.DataExcept:
                throw new RestrictionViolationException("data_except_contains_interleave");
            case RestrictionContext.List:
                throw new RestrictionViolationException("list_contains_interleave");
        }

        if (context == RestrictionContext.ElementRepeat)
        {
            context = RestrictionContext.ElementRepeatInterleave;
        }

        Alphabet a1 = alpha is not null && alpha.IsEmpty ? alpha : new Alphabet();
        Operand1.CheckRestrictions(context, dad, a1);
        if (a1.IsEmpty)
        {
            Operand2.CheckRestrictions(context, dad, a1);
        }
        else
        {
            Alphabet a2 = new();
            Operand2.CheckRestrictions(context, dad, a2);
            a1.CheckOverlap(a2);
            if (alpha is not null)
            {
                if (!ReferenceEquals(alpha, a1))
                {
                    alpha.AddAlphabet(a1);
                }

                alpha.AddAlphabet(a2);
            }
        }

        if (!ContentTypeGroupable(Operand1.ContentType, Operand2.ContentType))
        {
            throw new RestrictionViolationException("interleave_string");
        }

        if (Operand1.ContentType == ContentType.Mixed && Operand2.ContentType == ContentType.Mixed)
        {
            throw new RestrictionViolationException("interleave_text_overlap");
        }
    }

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseInterleave(this);
}

internal sealed class AfterPattern(Pattern p1, Pattern p2)
    : BinaryPattern(false, CombineHashCode(AfterHashCode, Identity(p1), Identity(p2)), p1, p2)
{
    public override bool IsNotAllowed => Operand1.IsNotAllowed;

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseAfter(this);
}

internal sealed class OneOrMorePattern(Pattern p)
    : Pattern(p.IsNullable, p.ContentType, CombineHashCode(OneOrMoreHashCode, Identity(p)))
{
    public Pattern Operand { get; } = p;

    public override Pattern Expand(SchemaPatternBuilder b)
    {
        Pattern ep = Operand.Expand(b);
        return !ReferenceEquals(ep, Operand) ? b.MakeOneOrMore(ep) : this;
    }

    public override void CheckRecursion(int depth, JingLocalizer localizer) => Operand.CheckRecursion(depth, localizer);

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        switch (context)
        {
            case RestrictionContext.Start:
                throw new RestrictionViolationException("start_contains_one_or_more");
            case RestrictionContext.DataExcept:
                throw new RestrictionViolationException("data_except_contains_one_or_more");
        }

        Operand.CheckRestrictions(context == RestrictionContext.Element ? RestrictionContext.ElementRepeat : context, dad, alpha);
        if (context != RestrictionContext.List && !ContentTypeGroupable(Operand.ContentType, Operand.ContentType))
        {
            throw new RestrictionViolationException("one_or_more_string");
        }
    }

    public override bool SamePattern(Pattern other) => other is OneOrMorePattern o && ReferenceEquals(Operand, o.Operand);

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseOneOrMore(this);
}

internal sealed class AttributePattern(NameClass nameClass, Pattern value, ILocator? locator, string? defaultValue)
    : Pattern(false, ContentType.Empty, CombineHashCode(AttributeHashCode, nameClass.GetHashCode(), Identity(value)))
{
    public NameClass NameClass { get; } = nameClass;

    public Pattern Content { get; } = value;

    public ILocator? Locator { get; } = locator;

    public string? DefaultValue { get; } = defaultValue;

    public override Pattern Expand(SchemaPatternBuilder b)
    {
        Pattern ep = Content.Expand(b);
        return !ReferenceEquals(ep, Content) ? b.MakeAttribute(NameClass, ep, Locator, DefaultValue) : this;
    }

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        switch (context)
        {
            case RestrictionContext.Start:
                throw new RestrictionViolationException("start_contains_attribute");
            case RestrictionContext.Element:
                if (NameClass.IsOpen)
                {
                    throw new RestrictionViolationException("open_name_class_not_repeated");
                }

                break;
            case RestrictionContext.ElementRepeatGroup:
                throw new RestrictionViolationException("one_or_more_contains_group_contains_attribute");
            case RestrictionContext.ElementRepeatInterleave:
                throw new RestrictionViolationException("one_or_more_contains_interleave_contains_attribute");
            case RestrictionContext.List:
                throw new RestrictionViolationException("list_contains_attribute");
            case RestrictionContext.Attribute:
                throw new RestrictionViolationException("attribute_contains_attribute");
            case RestrictionContext.DataExcept:
                throw new RestrictionViolationException("data_except_contains_attribute");
        }

        dad!.AddAttribute(NameClass);
        try
        {
            Content.CheckRestrictions(RestrictionContext.Attribute, null, null);
        }
        catch (RestrictionViolationException e)
        {
            e.MaybeSetLocator(Locator);
            throw;
        }
    }

    public override bool SamePattern(Pattern other) =>
        other is AttributePattern ap
        && NameClass.Equals(ap.NameClass)
        && ReferenceEquals(Content, ap.Content)
        && string.Equals(DefaultValue, ap.DefaultValue, System.StringComparison.Ordinal);

    public override void CheckRecursion(int depth, JingLocalizer localizer) => Content.CheckRecursion(depth, localizer);

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseAttribute(this);
}

internal sealed class ElementPattern : Pattern
{
    private readonly NameClass _origNameClass;
    private bool _expanded;
    private bool _checkedRestrictions;

    public ElementPattern(NameClass nameClass, Pattern p, ILocator? locator)
        : base(false, ContentType.Element, CombineHashCode(ElementHashCode, nameClass.GetHashCode(), Identity(p)))
    {
        NameClass = nameClass;
        _origNameClass = nameClass;
        Content = p;
        Locator = locator;
    }

    public NameClass NameClass { get; private set; }

    public Pattern Content { get; set; }

    public ILocator? Locator { get; }

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        alpha?.AddElement(_origNameClass);
        if (_checkedRestrictions)
        {
            return;
        }

        switch (context)
        {
            case RestrictionContext.DataExcept:
                throw new RestrictionViolationException("data_except_contains_element");
            case RestrictionContext.List:
                throw new RestrictionViolationException("list_contains_element");
            case RestrictionContext.Attribute:
                throw new RestrictionViolationException("attribute_contains_element");
        }

        _checkedRestrictions = true;
        try
        {
            Content.CheckRestrictions(RestrictionContext.Element, new DuplicateAttributeDetector(), null);
        }
        catch (RestrictionViolationException e)
        {
            _checkedRestrictions = false;
            e.MaybeSetLocator(Locator);
            throw;
        }
    }

    public override Pattern Expand(SchemaPatternBuilder b)
    {
        if (!_expanded)
        {
            _expanded = true;
            Content = Content.Expand(b);
            if (Content.IsNotAllowed)
            {
                NameClass = new NullNameClass();
            }
        }

        return this;
    }

    public override bool SamePattern(Pattern other) =>
        other is ElementPattern ep && NameClass.Equals(ep.NameClass) && ReferenceEquals(Content, ep.Content);

    public override void CheckRecursion(int depth, JingLocalizer localizer) => Content.CheckRecursion(depth + 1, localizer);

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseElement(this);
}

internal sealed class EmptyPattern() : Pattern(true, ContentType.Empty, EmptyHashCode)
{
    public override bool SamePattern(Pattern other) => other is EmptyPattern;

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseEmpty(this);

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        switch (context)
        {
            case RestrictionContext.DataExcept:
                throw new RestrictionViolationException("data_except_contains_empty");
            case RestrictionContext.Start:
                throw new RestrictionViolationException("start_contains_empty");
        }
    }
}

internal sealed class TextPattern() : Pattern(true, ContentType.Mixed, TextHashCode)
{
    public override bool SamePattern(Pattern other) => other is TextPattern;

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseText(this);

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        switch (context)
        {
            case RestrictionContext.DataExcept:
                throw new RestrictionViolationException("data_except_contains_text");
            case RestrictionContext.Start:
                throw new RestrictionViolationException("start_contains_text");
            case RestrictionContext.List:
                throw new RestrictionViolationException("list_contains_text");
        }
    }
}

internal class NotAllowedPattern() : Pattern(false, ContentType.Empty, NotAllowedHashCode)
{
    public override bool IsNotAllowed => true;

    // Has to tell an UnexpandedNotAllowedPattern apart.
    public override bool SamePattern(Pattern other) => other.GetType() == GetType();

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseNotAllowed(this);
}

/// <summary>The <c>notAllowed</c> of a schema before expansion: it must not make its parents not allowed yet (a reference may still be filled in).</summary>
internal sealed class UnexpandedNotAllowedPattern : NotAllowedPattern
{
    public override bool IsNotAllowed => false;

    public override Pattern Expand(SchemaPatternBuilder b) => b.MakeNotAllowed();
}

internal sealed class ErrorPattern() : Pattern(false, ContentType.Empty, ErrorHashCode)
{
    public override bool SamePattern(Pattern other) => other is ErrorPattern;

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseError(this);
}

internal abstract class StringPattern(int hashCode) : Pattern(false, ContentType.Data, hashCode);

internal class DataPattern(IDatatype datatype, Name datatypeName, IReadOnlyList<string> parameters)
    : StringPattern(CombineHashCode(DataHashCode, Identity(datatype)))
{
    public IDatatype Datatype { get; } = datatype;

    public Name DatatypeName { get; } = datatypeName;

    public IReadOnlyList<string> Parameters { get; } = parameters;

    public bool AllowsAnyString => Datatype is IDatatype2 dt2 && dt2.AlwaysValid;

    public override bool SamePattern(Pattern other) =>
        other.GetType() == GetType() && ReferenceEquals(Datatype, ((DataPattern)other).Datatype);

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseData(this);

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        if (context == RestrictionContext.Start)
        {
            throw new RestrictionViolationException("start_contains_data");
        }
    }
}

internal sealed class DataExceptPattern(IDatatype datatype, Name datatypeName, IReadOnlyList<string> parameters, Pattern except, ILocator? locator)
    : DataPattern(datatype, datatypeName, parameters)
{
    public Pattern Except { get; } = except;

    public ILocator? Locator { get; } = locator;

    public override bool SamePattern(Pattern other) =>
        base.SamePattern(other) && Except.SamePattern(((DataExceptPattern)other).Except);

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseDataExcept(this);

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        base.CheckRestrictions(context, dad, alpha);
        try
        {
            Except.CheckRestrictions(RestrictionContext.DataExcept, null, null);
        }
        catch (RestrictionViolationException e)
        {
            e.MaybeSetLocator(Locator);
            throw;
        }
    }
}

internal sealed class ValuePattern(IDatatype datatype, Name datatypeName, object value, string stringValue)
    : StringPattern(CombineHashCode(ValueHashCode, datatype.ValueHashCode(value)))
{
    public IDatatype Datatype { get; } = datatype;

    public Name DatatypeName { get; } = datatypeName;

    public object Value { get; } = value;

    public string StringValue { get; } = stringValue;

    public override bool SamePattern(Pattern other) =>
        other.GetType() == GetType()
        && other is ValuePattern vp
        && ReferenceEquals(Datatype, vp.Datatype)
        && Datatype.SameValue(Value, vp.Value);

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseValue(this);

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        if (context == RestrictionContext.Start)
        {
            throw new RestrictionViolationException("start_contains_value");
        }
    }
}

internal sealed class ListPattern(Pattern p, ILocator? locator)
    : Pattern(false, ContentType.Data, CombineHashCode(ListHashCode, Identity(p)))
{
    public Pattern Operand { get; } = p;

    public ILocator? Locator { get; } = locator;

    public override Pattern Expand(SchemaPatternBuilder b)
    {
        Pattern ep = Operand.Expand(b);
        return !ReferenceEquals(ep, Operand) ? b.MakeList(ep, Locator) : this;
    }

    public override void CheckRecursion(int depth, JingLocalizer localizer) => Operand.CheckRecursion(depth, localizer);

    public override bool SamePattern(Pattern other) => other is ListPattern lp && ReferenceEquals(Operand, lp.Operand);

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseList(this);

    public override void CheckRestrictions(RestrictionContext context, DuplicateAttributeDetector? dad, Alphabet? alpha)
    {
        switch (context)
        {
            case RestrictionContext.DataExcept:
                throw new RestrictionViolationException("data_except_contains_list");
            case RestrictionContext.Start:
                throw new RestrictionViolationException("start_contains_list");
            case RestrictionContext.List:
                throw new RestrictionViolationException("list_contains_list");
        }

        try
        {
            Operand.CheckRestrictions(RestrictionContext.List, dad, null);
        }
        catch (RestrictionViolationException e)
        {
            e.MaybeSetLocator(Locator);
            throw;
        }
    }
}

/// <summary>How a definition is combined with the others of the same name.</summary>
internal enum CombineType : byte
{
    None = 0,
    Choice = 1,
    Interleave = 2,
}

/// <summary>Whether a definition of an included grammar is kept or replaced by the including one.</summary>
internal enum ReplacementStatus : byte
{
    Keep = 0,
    Require = 1,
    Ignore = 2,
}

/// <summary>A named definition of a grammar (its <c>start</c> when the name is null), referred to by <c>ref</c>.</summary>
internal sealed class RefPattern(string? name) : Pattern
{
    private const int NotChecked = -1;
    private const int Checked = -2;
    private int _checkRecursionDepth = NotChecked;
    private bool _expanded;

    public Pattern? Target { get; set; }

    public ILocator? RefLocator { get; set; }

    public string? Name { get; } = name;

    public bool IsCombineImplicit { get; private set; }

    public CombineType CombineType { get; set; } = CombineType.None;

    public ReplacementStatus ReplacementStatus { get; set; } = ReplacementStatus.Keep;

    public void SetCombineImplicit() => IsCombineImplicit = true;

    public override void CheckRecursion(int depth, JingLocalizer localizer)
    {
        if (_checkRecursionDepth == NotChecked)
        {
            _checkRecursionDepth = depth;
            Target!.CheckRecursion(depth, localizer);
            _checkRecursionDepth = Checked;
        }
        else if (depth == _checkRecursionDepth)
        {
            throw new SaxParseException(localizer.Message("recursive_reference", Name), RefLocator);
        }
    }

    public override Pattern Expand(SchemaPatternBuilder b)
    {
        if (!_expanded)
        {
            Target = Target!.Expand(b);
            _expanded = true;
        }

        return Target!;
    }

    public override bool SamePattern(Pattern other) => false;

    public override T Apply<T>(IPatternFunction<T> f) => f.CaseRef(this);
}
