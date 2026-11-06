using System;
using System.Collections.Generic;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>A function over the kinds of pattern (Jing's <c>PatternFunction</c>).</summary>
internal interface IPatternFunction<out T>
{
    T CaseEmpty(EmptyPattern p);

    T CaseNotAllowed(NotAllowedPattern p);

    T CaseError(ErrorPattern p);

    T CaseGroup(GroupPattern p);

    T CaseInterleave(InterleavePattern p);

    T CaseChoice(ChoicePattern p);

    T CaseOneOrMore(OneOrMorePattern p);

    T CaseElement(ElementPattern p);

    T CaseAttribute(AttributePattern p);

    T CaseData(DataPattern p);

    T CaseDataExcept(DataExceptPattern p);

    T CaseValue(ValuePattern p);

    T CaseText(TextPattern p);

    T CaseList(ListPattern p);

    T CaseRef(RefPattern p);

    T CaseAfter(AfterPattern p);
}

/// <summary>A pattern function whose cases default to <see cref="CaseOther"/>.</summary>
internal abstract class AbstractPatternFunction<T> : IPatternFunction<T>
{
    public virtual T CaseEmpty(EmptyPattern p) => CaseOther(p);

    public virtual T CaseNotAllowed(NotAllowedPattern p) => CaseOther(p);

    public virtual T CaseError(ErrorPattern p) => CaseOther(p);

    public virtual T CaseGroup(GroupPattern p) => CaseOther(p);

    public virtual T CaseInterleave(InterleavePattern p) => CaseOther(p);

    public virtual T CaseChoice(ChoicePattern p) => CaseOther(p);

    public virtual T CaseOneOrMore(OneOrMorePattern p) => CaseOther(p);

    public virtual T CaseElement(ElementPattern p) => CaseOther(p);

    public virtual T CaseAttribute(AttributePattern p) => CaseOther(p);

    public virtual T CaseData(DataPattern p) => CaseOther(p);

    public virtual T CaseDataExcept(DataExceptPattern p) => CaseOther(p);

    public virtual T CaseValue(ValuePattern p) => CaseOther(p);

    public virtual T CaseText(TextPattern p) => CaseOther(p);

    public virtual T CaseList(ListPattern p) => CaseOther(p);

    public virtual T CaseAfter(AfterPattern p) => CaseOther(p);

    public virtual T CaseRef(RefPattern p) => CaseOther(p);

    public abstract T CaseOther(Pattern p);
}

/// <summary>Applies a change to the second operand of the after patterns of a choice of them (Jing's <c>ApplyAfterFunction</c>).</summary>
internal abstract class ApplyAfterFunction(ValidatorPatternBuilder builder) : AbstractPatternFunction<Pattern>
{
    protected ValidatorPatternBuilder Builder { get; } = builder;

    public override Pattern CaseAfter(AfterPattern p) => Builder.MakeAfter(p.Operand1, ApplyAfter(p.Operand2));

    public override Pattern CaseChoice(ChoicePattern p) => Builder.MakeChoice(p.Operand1.Apply(this), p.Operand2.Apply(this));

    public override Pattern CaseNotAllowed(NotAllowedPattern p) => p;

    public override Pattern CaseOther(Pattern p) =>
        throw new InvalidOperationException("ApplyAfterFunction applied to " + p.GetType().Name);

    protected abstract Pattern ApplyAfter(Pattern p);
}

/// <summary>An <see cref="ApplyAfterFunction"/> given as a delegate (Jing's anonymous classes).</summary>
internal sealed class DelegateApplyAfterFunction(ValidatorPatternBuilder builder, Func<Pattern, Pattern> apply) : ApplyAfterFunction(builder)
{
    protected override Pattern ApplyAfter(Pattern p) => apply(p);
}

/// <summary>The derivative at the end of the attributes of a start-tag: the attribute patterns are not allowed any more (Jing's <c>EndAttributesFunction</c>).</summary>
internal class EndAttributesFunction(ValidatorPatternBuilder builder) : AbstractPatternFunction<Pattern>
{
    public ValidatorPatternBuilder PatternBuilder { get; } = builder;

    public override Pattern CaseOther(Pattern p) => p;

    public override Pattern CaseGroup(GroupPattern p)
    {
        Pattern q1 = MemoApply(p.Operand1);
        Pattern q2 = MemoApply(p.Operand2);
        return ReferenceEquals(p.Operand1, q1) && ReferenceEquals(p.Operand2, q2) ? p : PatternBuilder.MakeGroup(q1, q2);
    }

    public override Pattern CaseInterleave(InterleavePattern p)
    {
        Pattern q1 = MemoApply(p.Operand1);
        Pattern q2 = MemoApply(p.Operand2);
        return ReferenceEquals(p.Operand1, q1) && ReferenceEquals(p.Operand2, q2) ? p : PatternBuilder.MakeInterleave(q1, q2);
    }

    public override Pattern CaseChoice(ChoicePattern p)
    {
        Pattern q1 = MemoApply(p.Operand1);
        Pattern q2 = MemoApply(p.Operand2);
        return ReferenceEquals(p.Operand1, q1) && ReferenceEquals(p.Operand2, q2) ? p : PatternBuilder.MakeChoice(q1, q2);
    }

    public override Pattern CaseOneOrMore(OneOrMorePattern p)
    {
        Pattern q1 = MemoApply(p.Operand);
        return ReferenceEquals(p.Operand, q1) ? p : PatternBuilder.MakeOneOrMore(q1);
    }

    public override Pattern CaseAfter(AfterPattern p)
    {
        Pattern q1 = MemoApply(p.Operand1);
        return ReferenceEquals(p.Operand1, q1) ? p : PatternBuilder.MakeAfter(q1, p.Operand2);
    }

    public override Pattern CaseAttribute(AttributePattern p) => PatternBuilder.MakeNotAllowed();

    public Pattern MemoApply(Pattern p) => Apply(PatternBuilder.GetPatternMemo(p)).Pattern;

    protected virtual PatternMemo Apply(PatternMemo memo) => memo.EndAttributes(this);
}

/// <summary>Like <see cref="EndAttributesFunction"/>, but a missing attribute is forgiven (recovery).</summary>
internal sealed class IgnoreMissingAttributesFunction(ValidatorPatternBuilder builder) : EndAttributesFunction(builder)
{
    public override Pattern CaseAttribute(AttributePattern p) => PatternBuilder.MakeEmpty();

    protected override PatternMemo Apply(PatternMemo memo) => memo.IgnoreMissingAttributes(this);
}

/// <summary>The derivative of text in mixed content (Jing's <c>MixedTextDerivFunction</c>).</summary>
internal sealed class MixedTextDerivFunction(ValidatorPatternBuilder builder) : EndAttributesFunction(builder)
{
    public override Pattern CaseText(TextPattern p) => p;

    public override Pattern CaseGroup(GroupPattern p)
    {
        Pattern p1 = p.Operand1;
        Pattern p2 = p.Operand2;
        Pattern q1 = MemoApply(p1);
        Pattern tem = ReferenceEquals(q1, p1) ? p : PatternBuilder.MakeGroup(q1, p2);
        return !p1.IsNullable ? tem : PatternBuilder.MakeChoice(tem, MemoApply(p2));
    }

    public override Pattern CaseInterleave(InterleavePattern p)
    {
        Pattern p1 = p.Operand1;
        Pattern p2 = p.Operand2;
        Pattern q1 = MemoApply(p1);
        Pattern q2 = MemoApply(p2);
        Pattern i1 = ReferenceEquals(q1, p1) ? p : PatternBuilder.MakeInterleave(q1, p2);
        Pattern i2 = ReferenceEquals(q2, p2) ? p : PatternBuilder.MakeInterleave(p1, q2);
        return PatternBuilder.MakeChoice(i1, i2);
    }

    public override Pattern CaseOneOrMore(OneOrMorePattern p) =>
        PatternBuilder.MakeGroup(MemoApply(p.Operand), PatternBuilder.MakeOptional(p));

    public override Pattern CaseOther(Pattern p) => PatternBuilder.MakeNotAllowed();

    protected override PatternMemo Apply(PatternMemo memo) => memo.MixedTextDeriv(this);
}

/// <summary>The content of an element with data content: its attributes and data, no elements (Jing's <c>TextOnlyFunction</c>).</summary>
internal sealed class TextOnlyFunction(ValidatorPatternBuilder builder) : EndAttributesFunction(builder)
{
    public override Pattern CaseAttribute(AttributePattern p) => p;

    public override Pattern CaseElement(ElementPattern p) => PatternBuilder.MakeNotAllowed();

    protected override PatternMemo Apply(PatternMemo memo) => memo.TextOnly(this);
}

/// <summary>The derivative of an end-tag (Jing's <c>EndTagDerivFunction</c>).</summary>
internal sealed class EndTagDerivFunction(ValidatorPatternBuilder builder) : AbstractPatternFunction<Pattern>
{
    public override Pattern CaseOther(Pattern p) => builder.MakeNotAllowed();

    public override Pattern CaseChoice(ChoicePattern p) => builder.MakeChoice(MemoApply(p.Operand1), MemoApply(p.Operand2));

    public override Pattern CaseAfter(AfterPattern p) => p.Operand1.IsNullable ? p.Operand2 : builder.MakeNotAllowed();

    private Pattern MemoApply(Pattern p) => builder.GetPatternMemo(p).EndTagDeriv(this).Pattern;
}

/// <summary>What follows the current element when it is given up: the second operands of the after patterns (Jing's <c>RecoverAfterFunction</c>).</summary>
internal sealed class RecoverAfterFunction(ValidatorPatternBuilder builder) : AbstractPatternFunction<Pattern>
{
    public override Pattern CaseOther(Pattern p) => throw new InvalidOperationException("recover after botch");

    public override Pattern CaseChoice(ChoicePattern p) => builder.MakeChoice(p.Operand1.Apply(this), p.Operand2.Apply(this));

    public override Pattern CaseAfter(AfterPattern p) => p.Operand2;
}

/// <summary>The derivative of the start of a start-tag (Jing's <c>StartTagOpenDerivFunction</c>).</summary>
internal class StartTagOpenDerivFunction(Name name, ValidatorPatternBuilder builder) : AbstractPatternFunction<Pattern>
{
    public Name Name { get; } = name;

    public ValidatorPatternBuilder PatternBuilder { get; } = builder;

    public override Pattern CaseChoice(ChoicePattern p) => PatternBuilder.MakeChoice(MemoApply(p.Operand1), MemoApply(p.Operand2));

    public override Pattern CaseGroup(GroupPattern p)
    {
        Pattern p1 = p.Operand1;
        Pattern p2 = p.Operand2;
        Pattern tem = MemoApply(p1).Apply(After(x => PatternBuilder.MakeGroup(x, p2)));
        return p1.IsNullable ? PatternBuilder.MakeChoice(tem, MemoApply(p2)) : tem;
    }

    public override Pattern CaseInterleave(InterleavePattern p)
    {
        Pattern p1 = p.Operand1;
        Pattern p2 = p.Operand2;
        return PatternBuilder.MakeChoice(
            MemoApply(p1).Apply(After(x => PatternBuilder.MakeInterleave(x, p2))),
            MemoApply(p2).Apply(After(x => PatternBuilder.MakeInterleave(p1, x))));
    }

    public override Pattern CaseAfter(AfterPattern p)
    {
        Pattern p2 = p.Operand2;
        return MemoApply(p.Operand1).Apply(After(x => PatternBuilder.MakeAfter(x, p2)));
    }

    public override Pattern CaseOneOrMore(OneOrMorePattern p) =>
        MemoApply(p.Operand).Apply(After(x => PatternBuilder.MakeGroup(x, PatternBuilder.MakeOptional(p))));

    public override Pattern CaseElement(ElementPattern p) =>
        !p.NameClass.Contains(Name) ? PatternBuilder.MakeNotAllowed() : PatternBuilder.MakeAfter(p.Content, PatternBuilder.MakeEmpty());

    public override Pattern CaseOther(Pattern p) => PatternBuilder.MakeNotAllowed();

    public Pattern MemoApply(Pattern p) => Apply(PatternBuilder.GetPatternMemo(p)).Pattern;

    protected virtual PatternMemo Apply(PatternMemo memo) => memo.StartTagOpenDeriv(this);

    protected DelegateApplyAfterFunction After(Func<Pattern, Pattern> apply) => new(PatternBuilder, apply);
}

/// <summary>Like <see cref="StartTagOpenDerivFunction"/>, but an element may also skip a required one before it (recovery).</summary>
internal sealed class StartTagOpenRecoverDerivFunction(Name name, ValidatorPatternBuilder builder) : StartTagOpenDerivFunction(name, builder)
{
    public override Pattern CaseGroup(GroupPattern p)
    {
        Pattern tem = base.CaseGroup(p);
        return p.Operand1.IsNullable ? tem : PatternBuilder.MakeChoice(tem, MemoApply(p.Operand2));
    }

    protected override PatternMemo Apply(PatternMemo memo) => memo.StartTagOpenRecoverDeriv(this);
}

/// <summary>The derivative of an attribute name (Jing's <c>StartAttributeDerivFunction</c>).</summary>
internal sealed class StartAttributeDerivFunction(Name name, ValidatorPatternBuilder builder) : StartTagOpenDerivFunction(name, builder)
{
    public override Pattern CaseElement(ElementPattern p) => PatternBuilder.MakeNotAllowed();

    public override Pattern CaseGroup(GroupPattern p)
    {
        Pattern p1 = p.Operand1;
        Pattern p2 = p.Operand2;
        return PatternBuilder.MakeChoice(
            MemoApply(p1).Apply(After(x => PatternBuilder.MakeGroup(x, p2))),
            MemoApply(p2).Apply(After(x => PatternBuilder.MakeGroup(p1, x))));
    }

    public override Pattern CaseAttribute(AttributePattern p) =>
        !p.NameClass.Contains(Name) ? PatternBuilder.MakeNotAllowed() : PatternBuilder.MakeAfter(p.Content, PatternBuilder.MakeEmpty());

    protected override PatternMemo Apply(PatternMemo memo) => memo.StartAttributeDeriv(this);
}

/// <summary>The content of the elements of a schema that match a name best: where an unexpected element is validated (Jing's <c>FindElementFunction</c>).</summary>
internal sealed class FindElementFunction : AbstractPatternFunction<bool>
{
    private readonly ValidatorPatternBuilder _builder;
    private readonly Name _name;
    private readonly HashSet<Pattern> _processed = new(ReferenceEqualityComparer.Instance);
    private Specificity _specificity = Specificity.None;
    private Pattern? _pattern;

    private FindElementFunction(ValidatorPatternBuilder builder, Name name)
    {
        _builder = builder;
        _name = name;
    }

    public static Pattern FindElement(ValidatorPatternBuilder builder, Name name, Pattern start)
    {
        FindElementFunction f = new(builder, name);
        start.Apply(f);
        return f._pattern ?? builder.MakeNotAllowed();
    }

    public override bool CaseGroup(GroupPattern p) => CaseBinary(p);

    public override bool CaseInterleave(InterleavePattern p) => CaseBinary(p);

    public override bool CaseChoice(ChoicePattern p) => CaseBinary(p);

    public override bool CaseOneOrMore(OneOrMorePattern p)
    {
        if (_processed.Add(p))
        {
            p.Operand.Apply(this);
        }

        return true;
    }

    public override bool CaseElement(ElementPattern p)
    {
        if (_processed.Add(p))
        {
            Specificity s = p.NameClass.ContainsSpecificity(_name);
            if (s > _specificity)
            {
                _specificity = s;
                _pattern = p.Content;
            }
            else if (s == _specificity && s != Specificity.None)
            {
                _pattern = _builder.MakeChoice(_pattern!, p.Content);
            }

            p.Content.Apply(this);
        }

        return true;
    }

    public override bool CaseOther(Pattern p) => true;

    private bool CaseBinary(BinaryPattern p)
    {
        if (_processed.Add(p))
        {
            p.Operand1.Apply(this);
            p.Operand2.Apply(this);
        }

        return true;
    }
}

/// <summary>The names of the elements or attributes that could come next (Jing's <c>PossibleNamesFunction</c>).</summary>
internal abstract class PossibleNamesFunction : AbstractPatternFunction<bool>
{
    private readonly UnionNameClassNormalizer _normalizer = new();

    public NormalizedNameClass ApplyTo(Pattern p)
    {
        _normalizer.NameClass = new NullNameClass();
        p.Apply(this);
        return _normalizer.Normalize();
    }

    public override bool CaseAfter(AfterPattern p) => p.Operand1.Apply(this);

    public override bool CaseChoice(ChoicePattern p) => CaseBinary(p);

    public override bool CaseInterleave(InterleavePattern p) => CaseBinary(p);

    public override bool CaseOneOrMore(OneOrMorePattern p) => p.Operand.Apply(this);

    public override bool CaseOther(Pattern p) => true;

    protected void Add(NameClass nc) => _normalizer.Add(nc);

    protected bool CaseBinary(BinaryPattern p)
    {
        p.Operand1.Apply(this);
        p.Operand2.Apply(this);
        return true;
    }
}

internal sealed class PossibleAttributeNamesFunction : PossibleNamesFunction
{
    public override bool CaseAttribute(AttributePattern p)
    {
        Add(p.NameClass);
        return true;
    }

    public override bool CaseGroup(GroupPattern p) => CaseBinary(p);
}

internal sealed class PossibleStartTagNamesFunction : PossibleNamesFunction
{
    public override bool CaseElement(ElementPattern p)
    {
        Add(p.NameClass);
        return true;
    }

    public override bool CaseGroup(GroupPattern p)
    {
        p.Operand1.Apply(this);
        if (p.Operand1.IsNullable)
        {
            p.Operand2.Apply(this);
        }

        return true;
    }
}

/// <summary>The elements or attributes that must come (Jing's <c>RequiredElementsOrAttributesFunction</c>): a name in a choice of names is not required.</summary>
internal abstract class RequiredElementsOrAttributesFunction : AbstractPatternFunction<HashSet<Name>>
{
    public override HashSet<Name> CaseOther(Pattern p) => [];

    public override HashSet<Name> CaseChoice(ChoicePattern p)
    {
        HashSet<Name> s1 = p.Operand1.Apply(this);
        HashSet<Name> s2 = p.Operand2.Apply(this);
        if (s1.Count == 0)
        {
            return s1;
        }

        if (s2.Count == 0)
        {
            return s2;
        }

        s1.IntersectWith(s2);
        return s1;
    }

    public override HashSet<Name> CaseInterleave(InterleavePattern p) => Union(p);

    public override HashSet<Name> CaseAfter(AfterPattern p) => p.Operand1.Apply(this);

    public override HashSet<Name> CaseOneOrMore(OneOrMorePattern p) => p.Operand.Apply(this);

    protected static HashSet<Name> CaseNamed(NameClass nc) => nc is SimpleNameClass snc ? [snc.Name] : [];

    protected HashSet<Name> Union(BinaryPattern p)
    {
        HashSet<Name> s1 = p.Operand1.Apply(this);
        HashSet<Name> s2 = p.Operand2.Apply(this);
        if (s1.Count == 0)
        {
            return s2;
        }

        if (s2.Count == 0)
        {
            return s1;
        }

        s1.UnionWith(s2);
        return s1;
    }
}

internal sealed class RequiredAttributesFunction : RequiredElementsOrAttributesFunction
{
    public override HashSet<Name> CaseAttribute(AttributePattern p) => CaseNamed(p.NameClass);

    public override HashSet<Name> CaseGroup(GroupPattern p) => Union(p);
}

internal sealed class RequiredElementsFunction : RequiredElementsOrAttributesFunction
{
    public override HashSet<Name> CaseElement(ElementPattern p) => CaseNamed(p.NameClass);

    public override HashSet<Name> CaseGroup(GroupPattern p) =>
        !p.Operand1.IsNullable ? p.Operand1.Apply(this) : p.Operand2.Apply(this);
}
