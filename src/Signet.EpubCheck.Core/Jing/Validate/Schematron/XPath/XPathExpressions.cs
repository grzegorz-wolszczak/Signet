using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Signet.EpubCheck.Core.Jing.Validate.Schematron.XPath;

/// <summary>An untyped value: what a node atomizes to (<c>xs:untypedAtomic</c>).</summary>
internal sealed record UntypedAtomic(string Value);

/// <summary>Where an expression is evaluated: the context item, position and size, the current node and the variables.</summary>
internal sealed class XPathContext
{
    public required object Item { get; init; }

    public int Position { get; init; } = 1;

    public int Size { get; init; } = 1;

    public required XdmNode Current { get; init; }

    public required IXPathVariables Variables { get; init; }

    public string? BaseUri { get; init; }

    public XPathContext With(object item, int position, int size) =>
        new() { Item = item, Position = position, Size = size, Current = Current, Variables = Variables, BaseUri = BaseUri };

    public XPathContext WithVariable(string name, IReadOnlyList<object> value) =>
        new() { Item = Item, Position = Position, Size = Size, Current = Current, Variables = new BoundVariable(name, value, Variables), BaseUri = BaseUri };

    public XdmNode ContextNode => Item as XdmNode ?? throw new XPathException("the context item is not a node");

    private sealed class BoundVariable(string name, IReadOnlyList<object> value, IXPathVariables outer) : IXPathVariables
    {
        public IReadOnlyList<object> Get(string variableName) => variableName == name ? value : outer.Get(variableName);
    }
}

/// <summary>The variables an expression can see.</summary>
internal interface IXPathVariables
{
    /// <exception cref="XPathException">There is no such variable.</exception>
    IReadOnlyList<object> Get(string name);
}

internal enum Axis
{
    Child,
    Descendant,
    Attribute,
    Self,
    DescendantOrSelf,
    FollowingSibling,
    Following,
    Parent,
    Ancestor,
    PrecedingSibling,
    Preceding,
    AncestorOrSelf,
}

/// <summary>
/// An expression. Values are sequences of items: nodes, strings, numbers (all <see cref="double"/>), booleans and
/// <see cref="UntypedAtomic"/>. Evaluation follows XPath 2.0 in XPath 1.0 compatibility mode, as Saxon runs the
/// <c>version="1.0"</c> stylesheet Jing compiles a Schematron schema to: a function that takes one value takes the first
/// item of a sequence, arithmetic is on doubles, a relational comparison compares numbers.
/// </summary>
internal abstract class XPathExpr
{
    public abstract IReadOnlyList<object> Evaluate(XPathContext context);

    public virtual bool UsesPosition => false;
}

internal sealed class LiteralExpr(object value) : XPathExpr
{
    public object Value { get; } = value;

    public override IReadOnlyList<object> Evaluate(XPathContext context) => [Value];
}

internal sealed class VariableExpr(string name) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context) => context.Variables.Get(name);
}

internal sealed class ContextItemExpr : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context) => [context.Item];
}

internal sealed class ParenthesizedExpr(XPathExpr inner) : XPathExpr
{
    public XPathExpr Inner { get; } = inner;

    public override IReadOnlyList<object> Evaluate(XPathContext context) => Inner.Evaluate(context);

    public override bool UsesPosition => Inner.UsesPosition;
}

internal sealed class SequenceExpr(IReadOnlyList<XPathExpr> items) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context) => [.. items.SelectMany(i => i.Evaluate(context))];

    public override bool UsesPosition => items.Any(i => i.UsesPosition);
}

internal sealed class IfExpr(XPathExpr condition, XPathExpr then, XPathExpr otherwise) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context) =>
        XPathValues.EffectiveBoolean(condition.Evaluate(context)) ? then.Evaluate(context) : otherwise.Evaluate(context);

    public override bool UsesPosition => condition.UsesPosition || then.UsesPosition || otherwise.UsesPosition;
}

internal sealed class QuantifiedExpr(bool every, IReadOnlyList<(string Name, XPathExpr Domain)> bindings, XPathExpr satisfies) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context) => [Test(context, 0)];

    private bool Test(XPathContext context, int index)
    {
        if (index == bindings.Count)
        {
            return XPathValues.EffectiveBoolean(satisfies.Evaluate(context));
        }

        foreach (object item in bindings[index].Domain.Evaluate(context))
        {
            bool result = Test(context.WithVariable(bindings[index].Name, [item]), index + 1);
            if (result != every)
            {
                return !every;
            }
        }

        return every;
    }
}

internal sealed class ForExpr(IReadOnlyList<(string Name, XPathExpr Domain)> bindings, XPathExpr body) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        List<object> result = [];
        Add(context, 0, result);
        return result;
    }

    private void Add(XPathContext context, int index, List<object> result)
    {
        if (index == bindings.Count)
        {
            result.AddRange(body.Evaluate(context));
            return;
        }

        foreach (object item in bindings[index].Domain.Evaluate(context))
        {
            Add(context.WithVariable(bindings[index].Name, [item]), index + 1, result);
        }
    }
}

internal sealed class OrExpr(XPathExpr left, XPathExpr right) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context) =>
        [XPathValues.EffectiveBoolean(left.Evaluate(context)) || XPathValues.EffectiveBoolean(right.Evaluate(context))];

    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;
}

internal sealed class AndExpr(XPathExpr left, XPathExpr right) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context) =>
        [XPathValues.EffectiveBoolean(left.Evaluate(context)) && XPathValues.EffectiveBoolean(right.Evaluate(context))];

    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;
}

internal sealed class ComparisonExpr(string op, XPathExpr left, XPathExpr right) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        IReadOnlyList<object> l = left.Evaluate(context);
        IReadOnlyList<object> r = right.Evaluate(context);
        return op switch
        {
            "=" or "!=" or "<" or "<=" or ">" or ">=" => [XPathValues.GeneralCompare(op, l, r)],
            "is" or "<<" or ">>" => NodeCompare(l, r),
            _ => ValueCompare(l, r),
        };
    }

    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;

    private IReadOnlyList<object> NodeCompare(IReadOnlyList<object> l, IReadOnlyList<object> r)
    {
        if (l.Count == 0 || r.Count == 0)
        {
            return [];
        }

        XdmNode a = (XdmNode)l[0];
        XdmNode b = (XdmNode)r[0];
        return [op switch { "is" => ReferenceEquals(a, b), "<<" => a.Order < b.Order, _ => a.Order > b.Order }];
    }

    private IReadOnlyList<object> ValueCompare(IReadOnlyList<object> l, IReadOnlyList<object> r)
    {
        List<object> a = XPathValues.Atomize(l);
        List<object> b = XPathValues.Atomize(r);
        if (a.Count == 0 || b.Count == 0)
        {
            return [];
        }

        if (a.Count > 1 || b.Count > 1)
        {
            throw new XPathException("a value comparison needs single values");
        }

        object x = a[0] is UntypedAtomic ux ? ux.Value : a[0];
        object y = b[0] is UntypedAtomic uy ? uy.Value : b[0];
        int c = XPathValues.CompareAtomic(x, y);
        string general = op switch { "eq" => "=", "ne" => "!=", "lt" => "<", "le" => "<=", "gt" => ">", _ => ">=" };
        return [XPathValues.Test(general, c, x, y)];
    }
}

internal sealed class RangeExpr(XPathExpr from, XPathExpr to) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        List<object> f = XPathValues.Atomize(from.Evaluate(context));
        List<object> t = XPathValues.Atomize(to.Evaluate(context));
        if (f.Count == 0 || t.Count == 0)
        {
            return [];
        }

        long a = (long)XPathValues.ToNumber(f[0]);
        long b = (long)XPathValues.ToNumber(t[0]);
        List<object> result = [];
        for (long i = a; i <= b; i++)
        {
            result.Add((double)i);
        }

        return result;
    }
}

internal sealed class ArithmeticExpr(string op, XPathExpr left, XPathExpr right) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        double a = XPathValues.NumberOfFirst(left.Evaluate(context));
        double b = XPathValues.NumberOfFirst(right.Evaluate(context));
        return [op switch
        {
            "+" => a + b,
            "-" => a - b,
            "*" => a * b,
            "div" => a / b,
            "idiv" => Math.Truncate(a / b),
            _ => a % b,
        }];
    }

    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;
}

internal sealed class NegateExpr(XPathExpr operand) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context) => [-XPathValues.NumberOfFirst(operand.Evaluate(context))];

    public override bool UsesPosition => operand.UsesPosition;
}

internal sealed class SetExpr(string op, XPathExpr left, XPathExpr right) : XPathExpr
{
    public string Op { get; } = op;

    public XPathExpr Left { get; } = left;

    public XPathExpr Right { get; } = right;

    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        List<XdmNode> l = XPathValues.Nodes(Left.Evaluate(context));
        List<XdmNode> r = XPathValues.Nodes(Right.Evaluate(context));
        IEnumerable<XdmNode> result = Op switch
        {
            "union" => l.Concat(r),
            "intersect" => l.Where(r.Contains),
            _ => l.Where(n => !r.Contains(n)),
        };
        return XPathValues.InDocumentOrder(result);
    }

    public override bool UsesPosition => Left.UsesPosition || Right.UsesPosition;
}

/// <summary>A path: each step is evaluated for each item the previous steps give; nodes come out in document order.</summary>
internal sealed class PathExpr(bool absolute, IReadOnlyList<XPathExpr> steps) : XPathExpr
{
    public bool Absolute { get; } = absolute;

    public IReadOnlyList<XPathExpr> Steps { get; } = steps;

    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        IReadOnlyList<object> current = Absolute ? [context.ContextNode.Root] : [context.Item];
        int first = 0;
        if (!Absolute)
        {
            current = Steps[0].Evaluate(context);
            first = 1;
        }

        for (int i = first; i < Steps.Count; i++)
        {
            List<object> next = [];
            bool allNodes = true;
            for (int j = 0; j < current.Count; j++)
            {
                if (current[j] is not XdmNode)
                {
                    throw new XPathException("a path step applies to a value that is not a node");
                }

                foreach (object item in Steps[i].Evaluate(context.With(current[j], j + 1, current.Count)))
                {
                    allNodes &= item is XdmNode;
                    next.Add(item);
                }
            }

            current = allNodes ? XPathValues.InDocumentOrder(next.Cast<XdmNode>()) : next;
        }

        return current;
    }

    public override bool UsesPosition => !Absolute && Steps.Count > 0 && Steps[0].UsesPosition;
}

internal sealed class FilterExpr(XPathExpr primary, IReadOnlyList<XPathExpr> predicates) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        IReadOnlyList<object> items = primary.Evaluate(context);
        foreach (XPathExpr predicate in predicates)
        {
            items = XPathValues.ApplyPredicate(items, predicate, context);
        }

        return items;
    }

    public override bool UsesPosition => primary.UsesPosition;
}

internal abstract class NodeTest
{
    public abstract bool Matches(XdmNode node, Axis axis);
}

/// <summary>A name test: <c>p:name</c>, <c>p:*</c>, <c>*:name</c> or <c>*</c> (a null part matches anything).</summary>
internal sealed class NameTest(string? namespaceUri, string? localName) : NodeTest
{
    public override bool Matches(XdmNode node, Axis axis)
    {
        XdmNodeKind principal = axis == Axis.Attribute ? XdmNodeKind.Attribute : XdmNodeKind.Element;
        return node.Kind == principal
            && (namespaceUri is null || node.NamespaceUri == namespaceUri)
            && (localName is null || node.LocalName == localName);
    }
}

/// <summary>A kind test: <c>node()</c> (null), <c>text()</c>, <c>element()</c>, <c>attribute()</c>...</summary>
internal sealed class KindTest(string? kind) : NodeTest
{
    public override bool Matches(XdmNode node, Axis axis) => kind switch
    {
        null or "node" => true,
        "text" => node.Kind == XdmNodeKind.Text,
        "element" => node.Kind == XdmNodeKind.Element,
        "attribute" => node.Kind == XdmNodeKind.Attribute,
        "document-node" => node.Kind == XdmNodeKind.Document,
        _ => false,
    };
}

internal sealed class AxisStep(Axis axis, NodeTest test, IReadOnlyList<XPathExpr> predicates) : XPathExpr
{
    public Axis Axis { get; } = axis;

    public NodeTest Test { get; } = test;

    public IReadOnlyList<XPathExpr> Predicates { get; } = predicates;

    public static bool IsReverse(Axis axis) => axis is Axis.Parent or Axis.Ancestor or Axis.AncestorOrSelf or Axis.PrecedingSibling or Axis.Preceding;

    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        // The nodes in the order of the axis (reverse document order for a reverse axis), for the positions.
        IReadOnlyList<object> items = [.. AxisNodes(context.ContextNode).Where(n => Test.Matches(n, Axis))];
        foreach (XPathExpr predicate in Predicates)
        {
            items = XPathValues.ApplyPredicate(items, predicate, context);
        }

        return IsReverse(Axis) ? [.. items.Reverse()] : items;
    }

    public IEnumerable<XdmNode> AxisNodes(XdmNode node)
    {
        switch (Axis)
        {
            case Axis.Child:
                return node.Children;
            case Axis.Attribute:
                return node.Attributes;
            case Axis.Self:
                return [node];
            case Axis.Parent:
                return node.Parent is null ? [] : [node.Parent];
            case Axis.Descendant:
                return Descendants(node);
            case Axis.DescendantOrSelf:
                return node.Kind == XdmNodeKind.Attribute ? [node] : Descendants(node).Prepend(node);
            case Axis.Ancestor:
                return Ancestors(node);
            case Axis.AncestorOrSelf:
                return Ancestors(node).Prepend(node);
            case Axis.FollowingSibling:
                return Siblings(node).SkipWhile(n => !ReferenceEquals(n, node)).Skip(1);
            case Axis.PrecedingSibling:
                return Siblings(node).TakeWhile(n => !ReferenceEquals(n, node)).Reverse();
            case Axis.Following:
                return Descendants(node.Root).Where(n => n.Order > node.Order && !Ancestors(n).Contains(node) && n.Kind != XdmNodeKind.Attribute);
            default:
                return Descendants(node.Root).Where(n => n.Order < node.Order && !Ancestors(node).Contains(n)).Reverse();
        }
    }

    private static IEnumerable<XdmNode> Siblings(XdmNode node) => node.Kind == XdmNodeKind.Attribute || node.Parent is null ? [] : node.Parent.Children;

    private static IEnumerable<XdmNode> Descendants(XdmNode node)
    {
        foreach (XdmNode child in node.Children)
        {
            yield return child;
            foreach (XdmNode d in Descendants(child))
            {
                yield return d;
            }
        }
    }

    private static IEnumerable<XdmNode> Ancestors(XdmNode node)
    {
        for (XdmNode? p = node.Parent; p is not null; p = p.Parent)
        {
            yield return p;
        }
    }
}

internal sealed class FunctionCallExpr(string name, IReadOnlyList<XPathExpr> args) : XPathExpr
{
    public string Name { get; } = name;

    public IReadOnlyList<XPathExpr> Args { get; } = args;

    public override IReadOnlyList<object> Evaluate(XPathContext context) => XPathFunctions.Call(Name, Args, context);

    public override bool UsesPosition => Name is "position" or "last" || Args.Any(a => a.UsesPosition);
}

/// <summary>The conversions and comparisons of XPath values.</summary>
internal static class XPathValues
{
    public static List<object> Atomize(IReadOnlyList<object> items) =>
        [.. items.Select(i => i is XdmNode n ? new UntypedAtomic(n.StringValue) : i)];

    public static List<XdmNode> Nodes(IReadOnlyList<object> items) =>
        [.. items.Select(i => i as XdmNode ?? throw new XPathException("a set operator applies to a value that is not a node"))];

    public static List<object> InDocumentOrder(IEnumerable<XdmNode> nodes) =>
        [.. nodes.Distinct().OrderBy(n => n.Order)];

    public static bool EffectiveBoolean(IReadOnlyList<object> items)
    {
        if (items.Count == 0)
        {
            return false;
        }

        if (items[0] is XdmNode)
        {
            return true;
        }

        if (items.Count > 1)
        {
            throw new XPathException("the effective boolean value of a sequence of several values is not defined");
        }

        return items[0] switch
        {
            bool b => b,
            string s => s.Length > 0,
            UntypedAtomic u => u.Value.Length > 0,
            double d => d != 0 && !double.IsNaN(d),
            _ => true,
        };
    }

    public static string ToStringValue(object item) => item switch
    {
        XdmNode n => n.StringValue,
        string s => s,
        UntypedAtomic u => u.Value,
        bool b => b ? "true" : "false",
        double d => FormatNumber(d),
        _ => item.ToString() ?? string.Empty,
    };

    // xs:double as XPath writes it, integers without a point (count() and number() values are doubles here).
    public static string FormatNumber(double d)
    {
        if (double.IsNaN(d))
        {
            return "NaN";
        }

        if (double.IsInfinity(d))
        {
            return d > 0 ? "INF" : "-INF";
        }

        return d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
    }

    public static double ToNumber(object item) => item switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        _ => ParseNumber(ToStringValue(item)),
    };

    public static double ParseNumber(string s)
    {
        s = s.Trim(' ', '\t', '\n', '\r');
        return s switch
        {
            "INF" => double.PositiveInfinity,
            "-INF" => double.NegativeInfinity,
            _ => s.Length > 0 && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN,
        };
    }

    /// <summary>The first item as a number, NaN for none (1.0 compatibility mode).</summary>
    public static double NumberOfFirst(IReadOnlyList<object> items) => items.Count == 0 ? double.NaN : ToNumber(Atomize([items[0]])[0]);

    /// <summary>The first item as a string, "" for none (1.0 compatibility mode).</summary>
    public static string StringOfFirst(IReadOnlyList<object> items) => items.Count == 0 ? string.Empty : ToStringValue(items[0]);

    /// <summary>A general comparison in XPath 1.0 compatibility mode (XPath 2.0, 3.5.2).</summary>
    public static bool GeneralCompare(string op, IReadOnlyList<object> l, IReadOnlyList<object> r)
    {
        if ((l.Count == 1 && l[0] is bool) || (r.Count == 1 && r[0] is bool))
        {
            bool a = EffectiveBoolean(l);
            bool b = EffectiveBoolean(r);
            return Test(op, a.CompareTo(b), a, b);
        }

        List<object> x = Atomize(l);
        List<object> y = Atomize(r);
        bool relational = op is "<" or "<=" or ">" or ">=";
        foreach (object a in x)
        {
            foreach (object b in y)
            {
                object p = a;
                object q = b;
                if (relational || p is double || q is double)
                {
                    p = ToNumber(p);
                    q = ToNumber(q);
                }
                else
                {
                    p = p is UntypedAtomic up ? up.Value : p;
                    q = q is UntypedAtomic uq ? uq.Value : q;
                }

                if (Test(op, CompareAtomic(p, q), p, q))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static int CompareAtomic(object a, object b) => (a, b) switch
    {
        (double x, double y) => x.CompareTo(y),
        (bool x, bool y) => x.CompareTo(y),
        (double x, _) => x.CompareTo(ToNumber(b)),
        (_, double y) => ToNumber(a).CompareTo(y),
        _ => string.CompareOrdinal(ToStringValue(a), ToStringValue(b)),
    };

    // NaN is not equal, less or greater than anything.
    public static bool Test(string op, int c, object a, object b)
    {
        if ((a is double x && double.IsNaN(x)) || (b is double y && double.IsNaN(y)))
        {
            return op == "!=";
        }

        return op switch
        {
            "=" => c == 0,
            "!=" => c != 0,
            "<" => c < 0,
            "<=" => c <= 0,
            ">" => c > 0,
            _ => c >= 0,
        };
    }

    public static IReadOnlyList<object> ApplyPredicate(IReadOnlyList<object> items, XPathExpr predicate, XPathContext context)
    {
        List<object> result = [];
        for (int i = 0; i < items.Count; i++)
        {
            IReadOnlyList<object> value = predicate.Evaluate(context.With(items[i], i + 1, items.Count));
            bool keep = value.Count == 1 && value[0] is double d ? d == i + 1 : EffectiveBoolean(value);
            if (keep)
            {
                result.Add(items[i]);
            }
        }

        return result;
    }
}
