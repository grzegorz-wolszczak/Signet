using System;
using System.Collections.Generic;
using Signet.EpubCheck.Core.Jing.Datatype;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>Why a data derivative is <c>notAllowed</c> (Jing's <c>DataDerivFailure</c>).</summary>
internal sealed class DataDerivFailure : IEquatable<DataDerivFailure>
{
    private const int UnknownIndex = -1;

    // Not a valid instance of the datatype. The reason is worked out when it is asked for (see Resolve): most failures belong to an
    // alternative of a choice that another alternative made good, and throwing an exception for each of them was a large part of a check.
    public DataDerivFailure(DataPattern p, string str, IValidationContext vc)
        : this(p.Datatype, p.DatatypeName, p.Parameters, null, UnknownIndex)
    {
        _pendingString = str;
        _pendingContext = vc;
    }

    // Not a valid instance of the datatype of a value pattern.
    public DataDerivFailure(IDatatype dt, Name dtName, string str, IValidationContext vc)
        : this(dt, dtName, null, null, UnknownIndex)
    {
        _pendingString = str;
        _pendingContext = vc;
    }

    // Matched the except of a data pattern.
    public DataDerivFailure(DataExceptPattern p)
    {
        Datatype = p.Datatype;
        DatatypeName = p.DatatypeName;
        DatatypeParams = p.Parameters;
        Except = p.Except;
        Index = UnknownIndex;
    }

    // Not equal to the value of a value pattern.
    public DataDerivFailure(ValuePattern p)
    {
        Datatype = p.Datatype;
        DatatypeName = p.DatatypeName;
        Index = UnknownIndex;
        StringValue = p.StringValue;
        Value = p.Value;
    }

    private DataDerivFailure(IDatatype datatype, Name datatypeName, IReadOnlyList<string>? datatypeParams, string? message, int index)
    {
        Datatype = datatype;
        DatatypeName = datatypeName;
        DatatypeParams = datatypeParams;
        _message = message;
        _index = index == DatatypeException.Unknown ? UnknownIndex : index;
    }

    // What is needed to find out why the string is not valid, until someone asks. A failure is read in the call of the matcher that made it
    // (the matcher clears its list before every match), so the context has not moved on by then.
    private string? _pendingString;
    private IValidationContext? _pendingContext;
    private string? _message;
    private int _index;

    private void Resolve()
    {
        if (_pendingContext is null)
        {
            return;
        }

        IValidationContext vc = _pendingContext;
        string str = _pendingString!;
        _pendingContext = null;
        _pendingString = null;
        try
        {
            Datatype.CheckValid(str, vc);
        }
        catch (DatatypeException e)
        {
            _message = e.Detail;
            _index = e.Index == DatatypeException.Unknown ? UnknownIndex : e.Index;
        }
    }

    public IDatatype Datatype { get; }

    public Name DatatypeName { get; }

    public IReadOnlyList<string>? DatatypeParams { get; }

    public string? Message
    {
        get
        {
            Resolve();
            return _message;
        }
    }

    public string? StringValue { get; }

    public object? Value { get; }

    public Pattern? Except { get; }

    public int Index
    {
        get
        {
            Resolve();
            return _index;
        }

        private set => _index = value;
    }

    public int TokenIndex { get; private set; } = UnknownIndex;

    public int TokenStart { get; private set; } = UnknownIndex;

    public int TokenEnd { get; private set; } = UnknownIndex;

    public void SetToken(int tokenIndex, int tokenStart, int tokenEnd)
    {
        TokenIndex = tokenIndex;
        TokenStart = tokenStart;
        TokenEnd = tokenEnd;
        if (Index < 0)
        {
            Index += tokenStart;
        }
    }

    public bool Equals(DataDerivFailure? other) =>
        other is not null
        && ReferenceEquals(Datatype, other.Datatype)
        && string.Equals(Message, other.Message, StringComparison.Ordinal)
        && string.Equals(StringValue, other.StringValue, StringComparison.Ordinal)
        && ReferenceEquals(Except, other.Except)
        && TokenIndex == other.TokenIndex
        && Index == other.Index;

    public override bool Equals(object? obj) => Equals(obj as DataDerivFailure);

    public override int GetHashCode()
    {
        int hc = Pattern.Identity(Datatype);
        if (Message is not null)
        {
            hc ^= StringComparer.Ordinal.GetHashCode(Message);
        }

        if (StringValue is not null)
        {
            hc ^= StringComparer.Ordinal.GetHashCode(StringValue);
        }

        if (Except is not null)
        {
            hc ^= Pattern.Identity(Except);
        }

        return hc;
    }
}

/// <summary>The derivative of a pattern with respect to a string (Jing's <c>DataDerivFunction</c>). If the result is not <c>notAllowed</c>, no failure is added.</summary>
internal sealed class DataDerivFunction(string str, IValidationContext vc, ValidatorPatternBuilder builder, List<DataDerivFailure>? fail)
    : AbstractPatternFunction<Pattern>
{
    public static bool IsBlank(string str)
    {
        foreach (char c in str)
        {
            if (c is not ('\r' or '\n' or ' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    public override Pattern CaseText(TextPattern p) => p;

    public override Pattern CaseRef(RefPattern p) => MemoApply(p.Target!);

    public override Pattern CaseList(ListPattern p)
    {
        int len = str.Length;
        int tokenIndex = 0;
        int tokenStart = -1;
        PatternMemo memo = builder.GetPatternMemo(p.Operand);
        for (int i = 0; i < len; i++)
        {
            if (str[i] is '\r' or '\n' or ' ' or '\t')
            {
                if (tokenStart >= 0)
                {
                    memo = TokenDeriv(memo, tokenIndex++, tokenStart, i);
                    tokenStart = -1;
                }
            }
            else if (tokenStart < 0)
            {
                tokenStart = i;
            }
        }

        if (tokenStart >= 0)
        {
            memo = TokenDeriv(memo, tokenIndex++, tokenStart, len);
        }

        if (memo.Pattern.IsNullable)
        {
            return builder.MakeEmpty();
        }

        if (memo.IsNotAllowed)
        {
            return memo.Pattern;
        }

        // A pseudo-token to get some failures.
        TokenDeriv(memo, tokenIndex, len, len);
        return builder.MakeNotAllowed();
    }

    public override Pattern CaseValue(ValuePattern p)
    {
        IDatatype dt = p.Datatype;
        object? value = dt.CreateValue(str, vc);
        if (value is not null && dt.SameValue(p.Value, value))
        {
            return builder.MakeEmpty();
        }

        if (fail is not null)
        {
            if (value is null)
            {
                if (!dt.IsValid(str, vc))
                {
                    fail.Add(new DataDerivFailure(dt, p.DatatypeName, str, vc));
                }
            }
            else
            {
                fail.Add(new DataDerivFailure(p));
            }
        }

        return builder.MakeNotAllowed();
    }

    public override Pattern CaseData(DataPattern p)
    {
        if (p.AllowsAnyString)
        {
            return builder.MakeEmpty();
        }

        if (p.Datatype.IsValid(str, vc))
        {
            return builder.MakeEmpty();
        }

        fail?.Add(new DataDerivFailure(p, str, vc));
        return builder.MakeNotAllowed();
    }

    public override Pattern CaseDataExcept(DataExceptPattern p)
    {
        Pattern tem = CaseData(p);
        if (tem.IsNullable && MemoApply(p.Except).IsNullable)
        {
            fail?.Add(new DataDerivFailure(p));
            return builder.MakeNotAllowed();
        }

        return tem;
    }

    public override Pattern CaseAfter(AfterPattern p)
    {
        Pattern p1 = p.Operand1;
        int failStartSize = FailSize();
        if (MemoApplyWithFailure(p1).IsNullable)
        {
            return p.Operand2;
        }

        if (p1.IsNullable && IsBlank(str))
        {
            ClearFailures(failStartSize);
            return p.Operand2;
        }

        return builder.MakeNotAllowed();
    }

    public override Pattern CaseChoice(ChoicePattern p)
    {
        int failStartSize = FailSize();
        Pattern tem = builder.MakeChoice(MemoApplyWithFailure(p.Operand1), MemoApplyWithFailure(p.Operand2));
        if (!tem.IsNotAllowed)
        {
            ClearFailures(failStartSize);
        }

        return tem;
    }

    public override Pattern CaseGroup(GroupPattern p)
    {
        int failStartSize = FailSize();
        Pattern p1 = p.Operand1;
        Pattern p2 = p.Operand2;
        Pattern tem = builder.MakeGroup(MemoApplyWithFailure(p1), p2);
        if (p1.IsNullable)
        {
            tem = builder.MakeChoice(tem, MemoApplyWithFailure(p2));
        }

        if (!tem.IsNotAllowed)
        {
            ClearFailures(failStartSize);
        }

        return tem;
    }

    // list//interleave is prohibited, so this cannot happen in a list.
    public override Pattern CaseInterleave(InterleavePattern p)
    {
        Pattern p1 = p.Operand1;
        Pattern p2 = p.Operand2;
        return builder.MakeChoice(builder.MakeInterleave(MemoApply(p1), p2), builder.MakeInterleave(p1, MemoApply(p2)));
    }

    public override Pattern CaseOneOrMore(OneOrMorePattern p) =>
        builder.MakeGroup(MemoApplyWithFailure(p.Operand), builder.MakeOptional(p));

    public override Pattern CaseOther(Pattern p) => builder.MakeNotAllowed();

    private PatternMemo TokenDeriv(PatternMemo p, int tokenIndex, int start, int end)
    {
        int failStartSize = FailSize();
        PatternMemo deriv = p.DataDeriv(str[start..end], vc, fail);
        if (fail is not null && deriv.IsNotAllowed)
        {
            for (int i = fail.Count - 1; i >= failStartSize; --i)
            {
                fail[i].SetToken(tokenIndex, start, end);
            }
        }

        return deriv;
    }

    private Pattern MemoApply(Pattern p) => builder.GetPatternMemo(p).DataDeriv(str, vc).Pattern;

    private Pattern MemoApplyWithFailure(Pattern p) => builder.GetPatternMemo(p).DataDeriv(str, vc, fail).Pattern;

    private int FailSize() => fail?.Count ?? 0;

    private void ClearFailures(int failStartSize)
    {
        if (fail is not null && fail.Count > failStartSize)
        {
            fail.RemoveRange(failStartSize, fail.Count - failStartSize);
        }
    }
}

/// <summary>How the data derivatives of a pattern can be memoized (Jing's <c>DataDerivType</c>).</summary>
internal abstract class DataDerivType
{
    public abstract DataDerivType Copy();

    public abstract DataDerivType Combine(DataDerivType ddt);

    public virtual PatternMemo DataDeriv(ValidatorPatternBuilder builder, Pattern p, string str, IValidationContext vc,
        List<DataDerivFailure>? fail) =>
        builder.GetPatternMemo(p.Apply(new DataDerivFunction(str, vc, builder, fail)));
}

/// <summary>A pattern whose derivative is the same for every string.</summary>
internal sealed class SingleDataDerivType : DataDerivType
{
    private PatternMemo? _memo;

    // This type never adds failures.
    public override PatternMemo DataDeriv(ValidatorPatternBuilder builder, Pattern p, string str, IValidationContext vc,
        List<DataDerivFailure>? fail) =>
        _memo ??= base.DataDeriv(builder, p, str, vc, null);

    public override DataDerivType Copy() => new SingleDataDerivType();

    public override DataDerivType Combine(DataDerivType ddt) => ddt;
}

/// <summary>A pattern whose derivative depends only on whether the string is blank.</summary>
internal sealed class BlankDataDerivType : DataDerivType
{
    private PatternMemo? _blankMemo;
    private PatternMemo? _nonBlankMemo;

    public override PatternMemo DataDeriv(ValidatorPatternBuilder builder, Pattern p, string str, IValidationContext vc,
        List<DataDerivFailure>? fail)
    {
        if (DataDerivFunction.IsBlank(str))
        {
            if (_blankMemo is null || (fail is not null && _blankMemo.IsNotAllowed))
            {
                _blankMemo = base.DataDeriv(builder, p, str, vc, fail);
            }

            return _blankMemo;
        }

        if (_nonBlankMemo is null || (fail is not null && _nonBlankMemo.IsNotAllowed))
        {
            _nonBlankMemo = base.DataDeriv(builder, p, str, vc, fail);
        }

        return _nonBlankMemo;
    }

    public override DataDerivType Copy() => new BlankDataDerivType();

    public override DataDerivType Combine(DataDerivType ddt) =>
        ddt is BlankDataDerivType or SingleDataDerivType ? this : InconsistentDataDerivType.Instance;
}

/// <summary>A pattern whose derivative depends only on whether the string is valid for one datatype.</summary>
internal sealed class DataDataDerivType(DataPattern dp) : DataDerivType
{
    private readonly DataPattern _dp = dp;
    private PatternMemo? _validMemo;
    private PatternMemo? _invalidMemo;

    public override PatternMemo DataDeriv(ValidatorPatternBuilder builder, Pattern p, string str, IValidationContext vc,
        List<DataDerivFailure>? fail)
    {
        bool isValid = _dp.Datatype.IsValid(str, vc);
        DataDerivFailure? ddf = !isValid && fail is not null ? new DataDerivFailure(_dp, str, vc) : null;

        if (isValid)
        {
            if (_validMemo is null || (fail is not null && _validMemo.IsNotAllowed))
            {
                _validMemo = base.DataDeriv(builder, p, str, vc, fail);
            }

            return _validMemo;
        }

        if (_invalidMemo is null)
        {
            _invalidMemo = base.DataDeriv(builder, p, str, vc, fail);
        }
        else if (_invalidMemo.IsNotAllowed && ddf is not null)
        {
            fail!.Add(ddf);
        }

        return _invalidMemo;
    }

    public override DataDerivType Copy() => new DataDataDerivType(_dp);

    public override DataDerivType Combine(DataDerivType ddt)
    {
        if (ddt is DataDataDerivType d)
        {
            return ReferenceEquals(d._dp.Datatype, _dp.Datatype) ? this : InconsistentDataDerivType.Instance;
        }

        if (ddt is ValueDataDerivType v)
        {
            return ReferenceEquals(v.Datatype, _dp.Datatype) ? ddt : InconsistentDataDerivType.Instance;
        }

        return ddt.Combine(this);
    }
}

/// <summary>A pattern that is a choice of values of one datatype.</summary>
internal sealed class ValueDataDerivType(IDatatype dt, Name dtName) : DataDerivType
{
    private PatternMemo? _noValue;
    private Dictionary<DatatypeValue, PatternMemo>? _valueMap;

    public IDatatype Datatype { get; } = dt;

    public override DataDerivType Copy() => new ValueDataDerivType(Datatype, dtName);

    public override PatternMemo DataDeriv(ValidatorPatternBuilder builder, Pattern p, string str, IValidationContext vc,
        List<DataDerivFailure>? fail)
    {
        object? value = Datatype.CreateValue(str, vc);
        if (value is null)
        {
            if (_noValue is null)
            {
                _noValue = base.DataDeriv(builder, p, str, vc, fail);
            }
            else if (fail is not null && _noValue.IsNotAllowed && !Datatype.IsValid(str, vc))
            {
                fail.Add(new DataDerivFailure(Datatype, dtName, str, vc));
            }

            return _noValue;
        }

        DatatypeValue dtv = new(value, Datatype);
        _valueMap ??= [];
        if (!_valueMap.TryGetValue(dtv, out PatternMemo? tem))
        {
            tem = base.DataDeriv(builder, p, str, vc, fail);
            _valueMap[dtv] = tem;
        }
        else if (tem.IsNotAllowed && fail is not null)
        {
            base.DataDeriv(builder, p, str, vc, fail);
        }

        return tem;
    }

    public override DataDerivType Combine(DataDerivType ddt)
    {
        if (ddt is ValueDataDerivType v)
        {
            return ReferenceEquals(v.Datatype, Datatype) ? this : InconsistentDataDerivType.Instance;
        }

        return ddt.Combine(this);
    }
}

/// <summary>A pattern whose data derivatives cannot be shared: computed every time.</summary>
internal sealed class InconsistentDataDerivType : DataDerivType
{
    private InconsistentDataDerivType()
    {
    }

    public static InconsistentDataDerivType Instance { get; } = new();

    public override DataDerivType Combine(DataDerivType ddt) => this;

    public override DataDerivType Copy() => this;
}

/// <summary>Finds the <see cref="DataDerivType"/> of a pattern (Jing's <c>DataDerivTypeFunction</c>).</summary>
internal sealed class DataDerivTypeFunction(ValidatorPatternBuilder builder) : AbstractPatternFunction<DataDerivType>
{
    public static DataDerivType DataDerivTypeOf(ValidatorPatternBuilder builder, Pattern pattern) =>
        pattern.Apply(builder.DataDerivTypeFunction);

    public override DataDerivType CaseOther(Pattern p) => new SingleDataDerivType();

    public override DataDerivType CaseRef(RefPattern p) => Apply(p.Target!);

    public override DataDerivType CaseAfter(AfterPattern p)
    {
        DataDerivType ddt = Apply(p.Operand1);
        return !p.Operand1.IsNullable ? ddt : ddt.Combine(new BlankDataDerivType());
    }

    public override DataDerivType CaseChoice(ChoicePattern p) => CaseBinary(p);

    public override DataDerivType CaseGroup(GroupPattern p) => CaseBinary(p);

    public override DataDerivType CaseInterleave(InterleavePattern p) => CaseBinary(p);

    public override DataDerivType CaseOneOrMore(OneOrMorePattern p) => Apply(p.Operand);

    public override DataDerivType CaseList(ListPattern p) => InconsistentDataDerivType.Instance;

    public override DataDerivType CaseValue(ValuePattern p) => new ValueDataDerivType(p.Datatype, p.DatatypeName);

    public override DataDerivType CaseData(DataPattern p) => p.AllowsAnyString ? new SingleDataDerivType() : new DataDataDerivType(p);

    public override DataDerivType CaseDataExcept(DataExceptPattern p) =>
        p.AllowsAnyString ? Apply(p.Except) : new DataDataDerivType(p).Combine(Apply(p.Except));

    private DataDerivType CaseBinary(BinaryPattern p) => Apply(p.Operand1).Combine(Apply(p.Operand2));

    private DataDerivType Apply(Pattern p) => builder.GetPatternMemo(p).DataDerivType();
}

/// <summary>A value of a datatype, compared as the datatype compares its values.</summary>
internal sealed class DatatypeValue(object value, IDatatype dt) : IEquatable<DatatypeValue>
{
    private readonly object _value = value;
    private readonly IDatatype _dt = dt;

    public bool Equals(DatatypeValue? other) =>
        other is not null && ReferenceEquals(other._dt, _dt) && _dt.SameValue(_value, other._value);

    public override bool Equals(object? obj) => Equals(obj as DatatypeValue);

    public override int GetHashCode() => Pattern.Identity(_dt) ^ _dt.ValueHashCode(_value);
}
