using System.Collections.Generic;
using Signet.EpubCheck.Core.Jing.Datatype;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>Makes interned patterns, simplifying on the way (Jing's <c>PatternBuilder</c>).</summary>
internal class PatternBuilder
{
    private readonly EmptyPattern _empty;

    public PatternBuilder()
    {
        _empty = new EmptyPattern();
        NotAllowed = new NotAllowedPattern();
        Interner = new PatternInterner();
    }

    public PatternBuilder(PatternBuilder parent)
    {
        _empty = parent._empty;
        NotAllowed = parent.NotAllowed;
        Interner = new PatternInterner(parent.Interner);
    }

    protected NotAllowedPattern NotAllowed { get; }

    protected PatternInterner Interner { get; }

    public Pattern MakeEmpty() => _empty;

    public Pattern MakeNotAllowed() => NotAllowed;

    public virtual Pattern MakeGroup(Pattern p1, Pattern p2)
    {
        if (ReferenceEquals(p1, _empty))
        {
            return p2;
        }

        if (ReferenceEquals(p2, _empty))
        {
            return p1;
        }

        if (ReferenceEquals(p1, NotAllowed) || ReferenceEquals(p2, NotAllowed))
        {
            return NotAllowed;
        }

        return Interner.Intern(new GroupPattern(p1, p2));
    }

    public Pattern MakeInterleave(Pattern p1, Pattern p2)
    {
        if (ReferenceEquals(p1, _empty))
        {
            return p2;
        }

        if (ReferenceEquals(p2, _empty))
        {
            return p1;
        }

        if (ReferenceEquals(p1, NotAllowed) || ReferenceEquals(p2, NotAllowed))
        {
            return NotAllowed;
        }

        return Interner.Intern(new InterleavePattern(p1, p2));
    }

    public virtual Pattern MakeChoice(Pattern p1, Pattern p2)
    {
        if (ReferenceEquals(p1, _empty) && p2.IsNullable)
        {
            return p2;
        }

        if (ReferenceEquals(p2, _empty) && p1.IsNullable)
        {
            return p1;
        }

        return Interner.Intern(new ChoicePattern(p1, p2));
    }

    public virtual Pattern MakeOneOrMore(Pattern p)
    {
        if (ReferenceEquals(p, _empty) || ReferenceEquals(p, NotAllowed) || p is OneOrMorePattern)
        {
            return p;
        }

        return Interner.Intern(new OneOrMorePattern(p));
    }

    public Pattern MakeOptional(Pattern p) => MakeChoice(p, _empty);

    public Pattern MakeZeroOrMore(Pattern p) => MakeOptional(MakeOneOrMore(p));
}

/// <summary>The builder of the patterns of a schema (Jing's <c>SchemaPatternBuilder</c>).</summary>
internal sealed class SchemaPatternBuilder : PatternBuilder
{
    private readonly UnexpandedNotAllowedPattern _unexpandedNotAllowed = new();
    private readonly TextPattern _text = new();
    private readonly PatternInterner _schemaInterner = new();

    /// <summary>Whether a datatype of the schema is an ID type.</summary>
    public bool HasIdTypes { get; private set; }

    public Pattern MakeElement(NameClass nameClass, Pattern content, ILocator? loc) =>
        _schemaInterner.Intern(new ElementPattern(nameClass, content, loc));

    public Pattern MakeAttribute(NameClass nameClass, Pattern value, ILocator? loc, string? defaultValue = null)
    {
        if (ReferenceEquals(value, NotAllowed))
        {
            return value;
        }

        return _schemaInterner.Intern(new AttributePattern(nameClass, value, loc, defaultValue));
    }

    public Pattern MakeData(IDatatype dt, Name dtName, IReadOnlyList<string> parameters)
    {
        NoteDatatype(dt);
        return _schemaInterner.Intern(new DataPattern(dt, dtName, parameters));
    }

    public Pattern MakeDataExcept(IDatatype dt, Name dtName, IReadOnlyList<string> parameters, Pattern except, ILocator? loc)
    {
        NoteDatatype(dt);
        return _schemaInterner.Intern(new DataExceptPattern(dt, dtName, parameters, except, loc));
    }

    public Pattern MakeValue(IDatatype dt, Name dtName, object value, string stringValue)
    {
        NoteDatatype(dt);
        return _schemaInterner.Intern(new ValuePattern(dt, dtName, value, stringValue));
    }

    public Pattern MakeText() => _text;

    public override Pattern MakeOneOrMore(Pattern p) => ReferenceEquals(p, _text) ? p : base.MakeOneOrMore(p);

    public Pattern MakeUnexpandedNotAllowed() => _unexpandedNotAllowed;

    public Pattern MakeError() => _schemaInterner.Intern(new ErrorPattern());

    public override Pattern MakeChoice(Pattern p1, Pattern p2)
    {
        if (ReferenceEquals(p1, NotAllowed) || ReferenceEquals(p1, p2))
        {
            return p2;
        }

        if (ReferenceEquals(p2, NotAllowed))
        {
            return p1;
        }

        return base.MakeChoice(p1, p2);
    }

    public Pattern MakeList(Pattern p, ILocator? loc)
    {
        if (ReferenceEquals(p, NotAllowed))
        {
            return p;
        }

        return _schemaInterner.Intern(new ListPattern(p, loc));
    }

    public Pattern MakeMixed(Pattern p) => MakeInterleave(_text, p);

    private void NoteDatatype(IDatatype dt)
    {
        if (dt.IdType != IdType.Null)
        {
            HasIdTypes = true;
        }
    }
}

/// <summary>
/// The builder of the derivatives a validator computes (Jing's <c>ValidatorPatternBuilder</c>): it shares the schema's
/// patterns and memoizes every derivative of every pattern.
/// </summary>
internal sealed class ValidatorPatternBuilder : PatternBuilder
{
    private readonly Dictionary<Pattern, PatternMemo> _patternMemoMap = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Pattern> _choiceSet = new(ReferenceEqualityComparer.Instance);
    private readonly RemoveChoicesFunction _removeChoicesFunction;
    private readonly NoteChoicesFunction _noteChoicesFunction;

    public ValidatorPatternBuilder(PatternBuilder builder)
        : base(builder)
    {
        EndAttributesFunction = new EndAttributesFunction(this);
        IgnoreMissingAttributesFunction = new IgnoreMissingAttributesFunction(this);
        EndTagDerivFunction = new EndTagDerivFunction(this);
        MixedTextDerivFunction = new MixedTextDerivFunction(this);
        TextOnlyFunction = new TextOnlyFunction(this);
        RecoverAfterFunction = new RecoverAfterFunction(this);
        DataDerivTypeFunction = new DataDerivTypeFunction(this);
        _removeChoicesFunction = new RemoveChoicesFunction(this);
        _noteChoicesFunction = new NoteChoicesFunction(this);
    }

    public IPatternFunction<Pattern> EndAttributesFunction { get; }

    public IPatternFunction<Pattern> IgnoreMissingAttributesFunction { get; }

    public IPatternFunction<Pattern> EndTagDerivFunction { get; }

    public IPatternFunction<Pattern> MixedTextDerivFunction { get; }

    public IPatternFunction<Pattern> TextOnlyFunction { get; }

    public IPatternFunction<Pattern> RecoverAfterFunction { get; }

    public IPatternFunction<DataDerivType> DataDerivTypeFunction { get; }

    public IPatternFunction<HashSet<Name>> RequiredElementsFunction { get; } = new RequiredElementsFunction();

    public IPatternFunction<HashSet<Name>> RequiredAttributesFunction { get; } = new RequiredAttributesFunction();

    public PossibleNamesFunction PossibleStartTagNamesFunction { get; } = new PossibleStartTagNamesFunction();

    public PossibleNamesFunction PossibleAttributeNamesFunction { get; } = new PossibleAttributeNamesFunction();

    /// <summary>How many patterns have a memo: the size of what the builder remembers.</summary>
    public int MemoCount => _patternMemoMap.Count;

    public PatternMemo GetPatternMemo(Pattern p)
    {
        if (!_patternMemoMap.TryGetValue(p, out PatternMemo? memo))
        {
            memo = new PatternMemo(p, this);
            _patternMemoMap[p] = memo;
        }

        return memo;
    }

    public Pattern MakeAfter(Pattern p1, Pattern p2) => Interner.Intern(new AfterPattern(p1, p2));

    public override Pattern MakeChoice(Pattern p1, Pattern p2)
    {
        if (ReferenceEquals(p1, p2))
        {
            return p1;
        }

        if (ReferenceEquals(p1, NotAllowed))
        {
            return p2;
        }

        if (ReferenceEquals(p2, NotAllowed))
        {
            return p1;
        }

        if (p1 is not ChoicePattern)
        {
            if (p2.ContainsChoice(p1))
            {
                return p2;
            }
        }
        else if (p2 is not ChoicePattern)
        {
            if (p1.ContainsChoice(p2))
            {
                return p1;
            }
        }
        else
        {
            p1.Apply(_noteChoicesFunction);
            p2 = p2.Apply(_removeChoicesFunction);
            _choiceSet.Clear();
            if (ReferenceEquals(p2, NotAllowed))
            {
                return p1;
            }
        }

        if (p1 is AfterPattern ap1 && p2 is AfterPattern ap2)
        {
            if (ReferenceEquals(ap1.Operand1, ap2.Operand1))
            {
                return MakeAfter(ap1.Operand1, MakeChoice(ap1.Operand2, ap2.Operand2));
            }

            if (ReferenceEquals(ap1.Operand1, NotAllowed))
            {
                return ap2;
            }

            if (ReferenceEquals(ap2.Operand1, NotAllowed))
            {
                return ap1;
            }

            if (ReferenceEquals(ap1.Operand2, ap2.Operand2))
            {
                return MakeAfter(MakeChoice(ap1.Operand1, ap2.Operand1), ap1.Operand2);
            }
        }

        return base.MakeChoice(p1, p2);
    }

    private sealed class NoteChoicesFunction(ValidatorPatternBuilder builder) : AbstractPatternFunction<bool>
    {
        public override bool CaseOther(Pattern p) => builder._choiceSet.Add(p);

        public override bool CaseChoice(ChoicePattern p)
        {
            p.Operand1.Apply(this);
            p.Operand2.Apply(this);
            return true;
        }
    }

    private sealed class RemoveChoicesFunction(ValidatorPatternBuilder builder) : AbstractPatternFunction<Pattern>
    {
        public override Pattern CaseOther(Pattern p) => builder._choiceSet.Contains(p) ? builder.NotAllowed : p;

        public override Pattern CaseChoice(ChoicePattern p)
        {
            Pattern p1 = p.Operand1.Apply(this);
            Pattern p2 = p.Operand2.Apply(this);
            if (ReferenceEquals(p1, p.Operand1) && ReferenceEquals(p2, p.Operand2))
            {
                return p;
            }

            if (ReferenceEquals(p1, builder.NotAllowed))
            {
                return p2;
            }

            if (ReferenceEquals(p2, builder.NotAllowed))
            {
                return p1;
            }

            return builder.Interner.Intern(new ChoicePattern(p1, p2));
        }
    }
}

/// <summary>An open-addressing table that makes equal patterns the same object (Jing's <c>PatternInterner</c>).</summary>
internal sealed class PatternInterner
{
    private const int InitialSize = 256;
    private const float LoadFactor = 0.3f;
    private Pattern?[]? _table;
    private int _used;
    private int _usedLimit;

    public PatternInterner()
    {
    }

    public PatternInterner(PatternInterner parent)
    {
        _table = (Pattern?[]?)parent._table?.Clone();
        _used = parent._used;
        _usedLimit = parent._usedLimit;
    }

    public Pattern Intern(Pattern p)
    {
        int h;
        if (_table is null)
        {
            _table = new Pattern?[InitialSize];
            _usedLimit = (int)(InitialSize * LoadFactor);
            h = FirstIndex(p);
        }
        else
        {
            for (h = FirstIndex(p); _table[h] is { } existing; h = NextIndex(h))
            {
                if (p.SamePattern(existing))
                {
                    return existing;
                }
            }
        }

        if (_used >= _usedLimit)
        {
            Pattern?[] oldTable = _table;
            _table = new Pattern?[oldTable.Length << 1];
            for (int i = oldTable.Length; i > 0;)
            {
                --i;
                if (oldTable[i] is { } old)
                {
                    int j;
                    for (j = FirstIndex(old); _table[j] is not null; j = NextIndex(j))
                    {
                    }

                    _table[j] = old;
                }
            }

            for (h = FirstIndex(p); _table[h] is not null; h = NextIndex(h))
            {
            }

            _usedLimit = (int)(_table.Length * LoadFactor);
        }

        _used++;
        _table[h] = p;
        return p;
    }

    private int FirstIndex(Pattern p) => p.PatternHashCode & (_table!.Length - 1);

    private int NextIndex(int i) => i == 0 ? _table!.Length - 1 : i - 1;
}
