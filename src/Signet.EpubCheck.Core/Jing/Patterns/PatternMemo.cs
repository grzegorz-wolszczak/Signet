using System.Collections.Generic;
using Signet.EpubCheck.Core.Jing.Datatype;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>A pattern with its derivatives computed once (Jing's <c>PatternMemo</c>).</summary>
internal sealed class PatternMemo(Pattern pattern, ValidatorPatternBuilder builder)
{
    private PatternMemo? _memoEndAttributes;
    private PatternMemo? _memoTextOnly;
    private PatternMemo? _memoEndTagDeriv;
    private PatternMemo? _memoMixedTextDeriv;
    private PatternMemo? _memoIgnoreMissingAttributes;
    private Dictionary<Name, PatternMemo>? _startTagOpenDerivMap;
    private Dictionary<Name, PatternMemo>? _startTagOpenRecoverDerivMap;
    private Dictionary<Name, PatternMemo>? _startAttributeDerivMap;
    private DataDerivType? _memoDataDerivType;
    private PatternMemo? _memoRecoverAfter;
    private PatternMemo? _memoEmptyAfter;
    private NormalizedNameClass? _memoPossibleAttributeNames;
    private NormalizedNameClass? _memoPossibleStartTagNames;

    public Pattern Pattern { get; } = pattern;

    public ValidatorPatternBuilder PatternBuilder { get; } = builder;

    public bool IsNotAllowed { get; } = pattern.IsNotAllowed;

    public PatternMemo EndAttributes() => _memoEndAttributes ??= ApplyForPatternMemo(PatternBuilder.EndAttributesFunction);

    public PatternMemo EndAttributes(IPatternFunction<Pattern> f) => _memoEndAttributes ??= ApplyForPatternMemo(f);

    public PatternMemo IgnoreMissingAttributes() =>
        _memoIgnoreMissingAttributes ??= ApplyForPatternMemo(PatternBuilder.IgnoreMissingAttributesFunction);

    public PatternMemo IgnoreMissingAttributes(IPatternFunction<Pattern> f) => _memoIgnoreMissingAttributes ??= ApplyForPatternMemo(f);

    public PatternMemo TextOnly() => _memoTextOnly ??= ApplyForPatternMemo(PatternBuilder.TextOnlyFunction);

    public PatternMemo TextOnly(IPatternFunction<Pattern> f) => _memoTextOnly ??= ApplyForPatternMemo(f);

    public PatternMemo EndTagDeriv() => _memoEndTagDeriv ??= ApplyForPatternMemo(PatternBuilder.EndTagDerivFunction);

    public PatternMemo EndTagDeriv(IPatternFunction<Pattern> f) => _memoEndTagDeriv ??= ApplyForPatternMemo(f);

    public PatternMemo MixedTextDeriv() => _memoMixedTextDeriv ??= ApplyForPatternMemo(PatternBuilder.MixedTextDerivFunction);

    public PatternMemo MixedTextDeriv(IPatternFunction<Pattern> f) => _memoMixedTextDeriv ??= ApplyForPatternMemo(f);

    public PatternMemo StartTagOpenDeriv(Name name) =>
        Derive(ref _startTagOpenDerivMap, name, () => new StartTagOpenDerivFunction(name, PatternBuilder));

    public PatternMemo StartTagOpenDeriv(StartTagOpenDerivFunction f) => Derive(ref _startTagOpenDerivMap, f.Name, () => f);

    public PatternMemo StartTagOpenRecoverDeriv(Name name) =>
        Derive(ref _startTagOpenRecoverDerivMap, name, () => new StartTagOpenRecoverDerivFunction(name, PatternBuilder));

    public PatternMemo StartTagOpenRecoverDeriv(StartTagOpenRecoverDerivFunction f) =>
        Derive(ref _startTagOpenRecoverDerivMap, f.Name, () => f);

    public PatternMemo StartAttributeDeriv(Name name) =>
        Derive(ref _startAttributeDerivMap, name, () => new StartAttributeDerivFunction(name, PatternBuilder));

    public PatternMemo StartAttributeDeriv(StartAttributeDerivFunction f) => Derive(ref _startAttributeDerivMap, f.Name, () => f);

    public DataDerivType DataDerivType() =>
        _memoDataDerivType ??= DataDerivTypeFunction.DataDerivTypeOf(PatternBuilder, Pattern).Copy();

    public PatternMemo DataDeriv(string str, IValidationContext vc) => DataDerivType().DataDeriv(PatternBuilder, Pattern, str, vc, null);

    public PatternMemo DataDeriv(string str, IValidationContext vc, List<DataDerivFailure>? fail) =>
        DataDerivType().DataDeriv(PatternBuilder, Pattern, str, vc, fail);

    public PatternMemo RecoverAfter() => _memoRecoverAfter ??= ApplyForPatternMemo(PatternBuilder.RecoverAfterFunction);

    public PatternMemo EmptyAfter() => _memoEmptyAfter ??= ApplyForPatternMemo(new EmptyAfterFunction(PatternBuilder));

    public NormalizedNameClass PossibleStartTagNames() =>
        _memoPossibleStartTagNames ??= PatternBuilder.PossibleStartTagNamesFunction.ApplyTo(Pattern);

    public NormalizedNameClass PossibleAttributeNames() =>
        _memoPossibleAttributeNames ??= PatternBuilder.PossibleAttributeNamesFunction.ApplyTo(Pattern);

    private PatternMemo Derive(ref Dictionary<Name, PatternMemo>? map, Name name, System.Func<IPatternFunction<Pattern>> function)
    {
        map ??= [];
        if (map.TryGetValue(name, out PatternMemo? memo))
        {
            return memo;
        }

        memo = ApplyForPatternMemo(function());
        map[name] = memo;
        return memo;
    }

    private PatternMemo ApplyForPatternMemo(IPatternFunction<Pattern> f) => PatternBuilder.GetPatternMemo(Pattern.Apply(f));

    // The after patterns with an empty second operand; anything else stays as it is.
    private sealed class EmptyAfterFunction(ValidatorPatternBuilder builder) : ApplyAfterFunction(builder)
    {
        protected override Pattern ApplyAfter(Pattern p) => Builder.MakeEmpty();

        public override Pattern CaseOther(Pattern p) => p;
    }
}
