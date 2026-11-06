using System;
using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Jing.Patterns;

namespace Signet.EpubCheck.Core.Tests.Jing;

/// <summary>
/// Whether two simplified patterns, made by different builders, have the same structure: the same operators in the
/// same order, the same names, datatypes and values. Element patterns can be recursive; a pair of elements already
/// being compared counts as equal.
/// </summary>
internal sealed class PatternEquivalence
{
    private readonly HashSet<(ElementPattern, ElementPattern)> _elements = [];

    public static bool AreEquivalent(Pattern p1, Pattern p2) => new PatternEquivalence().Same(p1, p2);

    private bool Same(Pattern p1, Pattern p2)
    {
        p1 = Resolve(p1);
        p2 = Resolve(p2);
        if (p1.GetType() != p2.GetType())
        {
            return false;
        }

        return (p1, p2) switch
        {
            (BinaryPattern b1, BinaryPattern b2) => Same(b1.Operand1, b2.Operand1) && Same(b1.Operand2, b2.Operand2),
            (OneOrMorePattern o1, OneOrMorePattern o2) => Same(o1.Operand, o2.Operand),
            (ListPattern l1, ListPattern l2) => Same(l1.Operand, l2.Operand),
            (AttributePattern a1, AttributePattern a2) => a1.NameClass.Equals(a2.NameClass)
                && string.Equals(a1.DefaultValue, a2.DefaultValue, StringComparison.Ordinal)
                && Same(a1.Content, a2.Content),
            (ElementPattern e1, ElementPattern e2) => e1.NameClass.Equals(e2.NameClass)
                && (!_elements.Add((e1, e2)) || Same(e1.Content, e2.Content)),
            (DataExceptPattern d1, DataExceptPattern d2) => SameData(d1, d2) && Same(d1.Except, d2.Except),
            (DataPattern d1, DataPattern d2) => SameData(d1, d2),
            (ValuePattern v1, ValuePattern v2) => v1.DatatypeName.Equals(v2.DatatypeName)
                && v1.Datatype.SameValue(v1.Value, v2.Value),
            _ => true,
        };
    }

    private static bool SameData(DataPattern d1, DataPattern d2) =>
        d1.DatatypeName.Equals(d2.DatatypeName) && d1.Parameters.SequenceEqual(d2.Parameters, StringComparer.Ordinal);

    private static Pattern Resolve(Pattern p)
    {
        while (p is RefPattern { Target: { } target })
        {
            p = target;
        }

        return p;
    }
}
