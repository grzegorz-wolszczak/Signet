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
    // Item, position and size are settable so that a loop over a list of items can reuse one context instead of making one per item (see
    // Rebind): a context is never kept by what it is handed to, only read during the call.
    public required object Item { get; set; }

    public int Position { get; set; } = 1;

    public int Size { get; set; } = 1;

    public required XdmNode Current { get; init; }

    public required IXPathVariables Variables { get; init; }

    public string? BaseUri { get; init; }

    /// <summary>
    /// The values of the subexpressions that depend on nothing but the current node, worked out once for it (shared by the
    /// contexts made from this one: they have the same current node).
    /// </summary>
    public MemoStore Memo
    {
        get => _memo ??= new();
        init => _memo = value;
    }

    private MemoStore? _memo;

    /// <summary>Points this context at another item of a list; only for a context the caller made itself, for its own loop.</summary>
    public void Rebind(object item, int position, int size)
    {
        Item = item;
        Position = position;
        Size = size;
    }

    public XPathContext With(object item, int position, int size) =>
        new() { Item = item, Position = position, Size = size, Current = Current, Variables = Variables, BaseUri = BaseUri, Memo = Memo };

    public XPathContext WithVariable(string name, IReadOnlyList<object> value) =>
        new() { Item = Item, Position = Position, Size = Size, Current = Current, Variables = new BoundVariable(name, value, Variables), BaseUri = BaseUri, Memo = Memo };

    /// <summary>The memo of a context: made when the first value is stored.</summary>
    public sealed class MemoStore
    {
        private Dictionary<XPathExpr, IReadOnlyList<object>>? _values;

        public IReadOnlyList<object> GetOrAdd(XPathExpr expr, XPathContext context)
        {
            _values ??= new Dictionary<XPathExpr, IReadOnlyList<object>>(ReferenceEqualityComparer.Instance);
            if (!_values.TryGetValue(expr, out IReadOnlyList<object>? value))
            {
                value = expr.Evaluate(context);
                _values[expr] = value;
            }

            return value;
        }
    }

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

    /// <summary>The effective boolean value of the result: the same as <c>EffectiveBoolean(Evaluate(context))</c>, without the list for tests that know better.</summary>
    public virtual bool EvaluateBoolean(XPathContext context) => XPathValues.EffectiveBoolean(Evaluate(context));

    public virtual bool UsesPosition => false;

    /// <summary>
    /// What the value depends on besides the document and the current node: the context item (and position and size), and the
    /// variables. An expression that depends on neither has the same value wherever it is evaluated for one current node.
    /// Unknown kinds of expression depend on both.
    /// </summary>
    public Dependencies Depends => _depends ??= ComputeDepends();

    private Dependencies? _depends;

    protected virtual Dependencies ComputeDepends() => Dependencies.Item | Dependencies.Variables;

    /// <summary>Whether the value is a boolean (or an empty sequence) and never a number, so that a predicate of it is never a position.</summary>
    public virtual bool IsBoolean => false;

    /// <summary>The value, worked out once per current node when it depends on nothing else.</summary>
    protected static IReadOnlyList<object> EvaluateOnce(XPathExpr expr, XPathContext context) =>
        (expr.Depends & (Dependencies.Item | Dependencies.Variables)) == 0 && expr is not LiteralExpr ? context.Memo.GetOrAdd(expr, context) : expr.Evaluate(context);
}

[Flags]
internal enum Dependencies
{
    None = 0,
    Item = 1,
    Variables = 2,

    /// <summary>The current node (<c>current()</c>): the same wherever the expression is evaluated for one node, but not for another.</summary>
    Current = 4,
}

internal sealed class LiteralExpr(object value) : XPathExpr
{
    public object Value { get; } = value;

    public override IReadOnlyList<object> Evaluate(XPathContext context) => [Value];

    protected override Dependencies ComputeDepends() => Dependencies.None;
}

internal sealed class VariableExpr(string name) : XPathExpr
{
    public override IReadOnlyList<object> Evaluate(XPathContext context) => context.Variables.Get(name);

    protected override Dependencies ComputeDepends() => Dependencies.Variables;
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

    protected override Dependencies ComputeDepends() => Inner.Depends;

    public override bool IsBoolean => Inner.IsBoolean;
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

    public override bool IsBoolean => true;

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
    protected override Dependencies ComputeDepends() => left.Depends | right.Depends;

    public override bool IsBoolean => true;

    public override IReadOnlyList<object> Evaluate(XPathContext context) => XPathValues.Bool(EvaluateBoolean(context));

    public override bool EvaluateBoolean(XPathContext context) => left.EvaluateBoolean(context) || right.EvaluateBoolean(context);

    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;
}

internal sealed class AndExpr(XPathExpr left, XPathExpr right) : XPathExpr
{
    protected override Dependencies ComputeDepends() => left.Depends | right.Depends;

    public override bool IsBoolean => true;

    public override IReadOnlyList<object> Evaluate(XPathContext context) => XPathValues.Bool(EvaluateBoolean(context));

    public override bool EvaluateBoolean(XPathContext context) => left.EvaluateBoolean(context) && right.EvaluateBoolean(context);

    public override bool UsesPosition => left.UsesPosition || right.UsesPosition;
}

internal sealed class ComparisonExpr(string op, XPathExpr left, XPathExpr right) : XPathExpr
{
    public string Op => op;

    public XPathExpr Left => left;

    public XPathExpr Right => right;

    protected override Dependencies ComputeDepends() => left.Depends | right.Depends;

    public override bool IsBoolean => op is "=" or "!=" or "<" or "<=" or ">" or ">=" or "is" or "<<" or ">>" or "eq" or "ne" or "lt" or "le" or "gt" or "ge";

    public override bool EvaluateBoolean(XPathContext context) =>
        op is "=" or "!=" or "<" or "<=" or ">" or ">="
            ? XPathValues.GeneralCompare(op, EvaluateOnce(left, context), EvaluateOnce(right, context))
            : base.EvaluateBoolean(context);

    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        IReadOnlyList<object> l = EvaluateOnce(left, context);
        IReadOnlyList<object> r = EvaluateOnce(right, context);
        return op switch
        {
            "=" or "!=" or "<" or "<=" or ">" or ">=" => XPathValues.Bool(XPathValues.GeneralCompare(op, l, r)),
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
    protected override Dependencies ComputeDepends() => left.Depends | right.Depends;

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
    protected override Dependencies ComputeDepends() => operand.Depends;

    public override IReadOnlyList<object> Evaluate(XPathContext context) => [-XPathValues.NumberOfFirst(operand.Evaluate(context))];

    public override bool UsesPosition => operand.UsesPosition;
}

internal sealed class SetExpr(string op, XPathExpr left, XPathExpr right) : XPathExpr
{
    public string Op => op;

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

    // What the steps use of the variables; the item only matters for the first step of a relative path (the others are
    // evaluated on the nodes the steps before them found), and an absolute path starts at the root of the document.
    protected override Dependencies ComputeDepends()
    {
        {
            Dependencies depends = Absolute ? Dependencies.None : Steps[0].Depends;
            foreach (XPathExpr step in Steps)
            {
                depends |= step.Depends & Dependencies.Variables;
            }

            return depends;
        }
    }

    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        IReadOnlyList<object> current;
        int first = 0;
        if (!Absolute)
        {
            current = Steps[0].Evaluate(context);
            first = 1;
        }
        else if (TryEvaluateEverywhere(context, out IReadOnlyList<object>? found))
        {
            // //name[...] : the nodes the second step selects in the whole document, worked out once, then the predicates.
            current = found;
            first = 2;
        }
        else
        {
            current = [context.ContextNode.Root];
        }

        XPathContext? scratch = null;
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

                if (scratch is null)
                {
                    scratch = context.With(current[j], j + 1, current.Count);
                }
                else
                {
                    scratch.Rebind(current[j], j + 1, current.Count);
                }

                IReadOnlyList<object> produced = Steps[i].Evaluate(scratch);
                for (int k = 0; k < produced.Count; k++)
                {
                    allNodes &= produced[k] is XdmNode;
                    next.Add(produced[k]);
                }
            }

            current = allNodes ? XPathValues.InDocumentOrder(next) : next;
        }

        return current;
    }

    public override bool UsesPosition => !Absolute && Steps.Count > 0 && Steps[0].UsesPosition;

    // "//name" and "//@name" with predicates that are tests (so a predicate is never a position): the same as asking every node
    // for its children (attributes) and keeping the ones that pass, in document order. The nodes of the document that match the
    // step are listed once and kept, which is what makes a predicate on the list worth indexing (ApplyPredicate).
    private bool TryEvaluateEverywhere(XPathContext context, out IReadOnlyList<object> found)
    {
        found = [];
        if (Steps.Count < 2
            || Steps[0] is not AxisStep { Axis: Axis.DescendantOrSelf, Test: KindTest { Kind: null or "node" }, Predicates.Count: 0 }
            || Steps[1] is not AxisStep { Axis: Axis.Child or Axis.Attribute } step
            || step.Predicates.Any(p => !p.IsBoolean || p.UsesPosition)
            || context.ContextNode.Root is not XdmDocument document)
        {
            return false;
        }

        if (!document.Cache.TryGetValue(step, out object? cached))
        {
            StableList nodes = [];
            Collect(document, step, nodes);
            document.Cache[step] = cached = nodes;
        }

        IReadOnlyList<object> items = (StableList)cached;
        foreach (XPathExpr predicate in step.Predicates)
        {
            items = XPathValues.ApplyPredicate(items, predicate, context);
        }

        found = items;
        return true;
    }

    private static void Collect(XdmNode node, AxisStep step, List<object> into)
    {
        if (step.Axis == Axis.Attribute)
        {
            foreach (XdmNode attribute in node.Attributes)
            {
                if (step.Test.Matches(attribute, Axis.Attribute))
                {
                    into.Add(attribute);
                }
            }
        }

        foreach (XdmNode child in node.Children)
        {
            if (step.Axis == Axis.Child && step.Test.Matches(child, Axis.Child))
            {
                into.Add(child);
            }

            Collect(child, step, into);
        }
    }
}

/// <summary>A list of nodes that is kept (and so can be told from one made for a single evaluation): predicates over it can be indexed.</summary>
internal sealed class StableList : List<object>;

internal sealed class FilterExpr(XPathExpr primary, IReadOnlyList<XPathExpr> predicates) : XPathExpr
{
    protected override Dependencies ComputeDepends() => primary.Depends | predicates.Aggregate(Dependencies.None, (d, p) => d | (p.Depends & Dependencies.Variables));

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
    public string? Kind => kind;

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

    protected override Dependencies ComputeDepends() =>
        Dependencies.Item | Predicates.Aggregate(Dependencies.None, (d, p) => d | (p.Depends & Dependencies.Variables));

    public static bool IsReverse(Axis axis) => axis is Axis.Parent or Axis.Ancestor or Axis.AncestorOrSelf or Axis.PrecedingSibling or Axis.Preceding;

    public override IReadOnlyList<object> Evaluate(XPathContext context)
    {
        // The nodes in the order of the axis (reverse document order for a reverse axis), for the positions.
        List<object> selected = [];
        XdmNode origin = context.ContextNode;
        if (Axis is Axis.Child or Axis.Attribute)
        {
            IReadOnlyList<XdmNode> nodes = Axis == Axis.Child ? origin.Children : origin.Attributes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (Test.Matches(nodes[i], Axis))
                {
                    selected.Add(nodes[i]);
                }
            }
        }
        else
        {
            foreach (XdmNode n in AxisNodes(origin))
            {
                if (Test.Matches(n, Axis))
                {
                    selected.Add(n);
                }
            }
        }

        IReadOnlyList<object> items = selected;
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
    // The name without the prefix of the standard function namespace, cut once and not on every call.
    private readonly string _local = name.StartsWith("fn:", StringComparison.Ordinal) ? name[3..] : name;

    public string Name { get; } = name;

    public IReadOnlyList<XPathExpr> Args { get; } = args;

    public override IReadOnlyList<object> Evaluate(XPathContext context) => XPathFunctions.CallLocal(_local, Name, Args, context);

    public override bool EvaluateBoolean(XPathContext context) => _local switch
    {
        "not" => !Args[0].EvaluateBoolean(context),
        "boolean" => Args[0].EvaluateBoolean(context),
        "exists" => Args[0].Evaluate(context).Count > 0,
        "empty" => Args[0].Evaluate(context).Count == 0,
        _ => base.EvaluateBoolean(context),
    };

    public override bool UsesPosition => Name is "position" or "last" || Args.Any(a => a.UsesPosition);

    // The functions that take the context item when they are given no argument, and the ones about the position, depend on it.
    protected override Dependencies ComputeDepends()
    {
        {
            string local = Name.StartsWith("fn:", StringComparison.Ordinal) ? Name[3..] : Name;
            if (local is "position" or "last")
            {
                return Dependencies.Item;
            }

            if (local == "current")
            {
                return Dependencies.Current;
            }

            Dependencies depends = Args.Aggregate(Dependencies.None, (d, a) => d | a.Depends);
            return Args.Count == 0 && local is not ("true" or "false" or "current") ? depends | Dependencies.Item : depends;
        }
    }

    public override bool IsBoolean => Name is "exists" or "empty" or "not" or "boolean" or "true" or "false" or "contains" or "starts-with"
        or "ends-with" or "matches" or "fn:exists" or "fn:empty" or "fn:not" or "fn:boolean" or "fn:contains" or "fn:starts-with" or "fn:ends-with" or "fn:matches";
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

    /// <summary>The nodes of a list, each once, in document order. A list already in that order (a single step down the tree gives one) is returned as it is.</summary>
    public static List<object> InDocumentOrder(List<object> nodes)
    {
        bool ordered = true;
        for (int i = 1; i < nodes.Count && ordered; i++)
        {
            ordered = ((XdmNode)nodes[i - 1]).Order < ((XdmNode)nodes[i]).Order;
        }

        return ordered ? nodes : InDocumentOrder(nodes.Cast<XdmNode>());
    }

    private static readonly IReadOnlyList<object> s_true = new object[] { true };
    private static readonly IReadOnlyList<object> s_false = new object[] { false };

    /// <summary>The value of a boolean: one of two shared lists (nothing changes a value it was given).</summary>
    public static IReadOnlyList<object> Bool(bool value) => value ? s_true : s_false;

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

        // The items atomized: a node is its string value, as an untyped value would be (a number and a string compare as numbers, two
        // strings as strings), without making an UntypedAtomic for every node.
        bool relational = op is "<" or "<=" or ">" or ">=";
        if (l.Count == 1 && r.Count == 1)
        {
            return CompareItems(op, relational, Plain(l[0]), Plain(r[0]));
        }

        object[] y = new object[r.Count];
        for (int i = 0; i < y.Length; i++)
        {
            y[i] = Plain(r[i]);
        }

        for (int i = 0; i < l.Count; i++)
        {
            object a = Plain(l[i]);
            foreach (object b in y)
            {
                if (CompareItems(op, relational, a, b))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static object Plain(object item) => item switch
    {
        XdmNode n => n.StringValue,
        UntypedAtomic u => u.Value,
        _ => item,
    };

    private static bool CompareItems(string op, bool relational, object p, object q)
    {
        if (relational || p is double || q is double)
        {
            p = ToNumber(p);
            q = ToNumber(q);
        }

        return Test(op, CompareAtomic(p, q), p, q);
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
        if (items.Count >= MinIndexedItems && TryApplyEquality(items, predicate, context, out IReadOnlyList<object>? indexed))
        {
            return indexed;
        }

        List<object> result = [];
        XPathContext? scratch = null;
        for (int i = 0; i < items.Count; i++)
        {
            if (scratch is null)
            {
                scratch = context.With(items[i], i + 1, items.Count);
            }
            else
            {
                scratch.Rebind(items[i], i + 1, items.Count);
            }

            bool keep;
            if (predicate.IsBoolean)
            {
                keep = predicate.EvaluateBoolean(scratch);
            }
            else
            {
                IReadOnlyList<object> value = predicate.Evaluate(scratch);
                keep = value.Count == 1 && value[0] is double d ? d == i + 1 : EffectiveBoolean(value);
            }

            if (keep)
            {
                result.Add(items[i]);
            }
        }

        return result;
    }

    // [A = B] over a list that is kept, where A depends on the item only and B on nothing: A is worked out for every node of the
    // list once (a table from the value to the nodes), and each later evaluation only looks B up. Without it a test like
    // "//@id[. = current()/@id]" for every id of a book is quadratic. Only strings are compared this way (a number or a boolean
    // changes what "=" means), so anything else is left to the slow path.
    private static bool TryApplyEquality(IReadOnlyList<object> items, XPathExpr predicate, XPathContext context, out IReadOnlyList<object> result)
    {
        result = items;
        if (predicate is not ComparisonExpr { Op: "=" } comparison
            || comparison.Left.Depends != Dependencies.Item
            || (comparison.Right.Depends & (Dependencies.Item | Dependencies.Variables)) != 0
            || comparison.Left.UsesPosition
            || context.Current.Root is not XdmDocument document)
        {
            return false;
        }

        IReadOnlyList<object> wanted = context.Memo.GetOrAdd(comparison.Right, context);
        if (wanted.Any(w => w is double or bool))
        {
            return false;
        }

        // The table is kept for the predicate and the list it was last applied to. A list that is applied to again (a variable's
        // value, the nodes of a step worked out once) gets its table the second time it is seen; one made for a single evaluation
        // never does, so it costs nothing.
        if (!document.Cache.TryGetValue(predicate, out object? known) || known is not IndexEntry entry || !ReferenceEquals(entry.List, items))
        {
            document.Cache[predicate] = new IndexEntry(items);
            return false;
        }

        entry.Index ??= BuildIndex(items, comparison.Left, context);
        if (entry.Index is not Dictionary<string, List<int>> index)
        {
            return false;
        }

        SortedSet<int> positions = [];
        foreach (object value in wanted)
        {
            if (index.TryGetValue(ToStringValue(value), out List<int>? at))
            {
                positions.UnionWith(at);
            }
        }

        result = positions.Select(i => items[i]).ToList();
        return true;
    }

    // The positions of the list by the strings the left side has for the nodes; NoIndex when a value is not a string (or a node).
    private static object BuildIndex(IReadOnlyList<object> items, XPathExpr left, XPathContext context)
    {
        Dictionary<string, List<int>> index = new(StringComparer.Ordinal);
        XPathContext scratch = context.With(items.Count > 0 ? items[0] : context.Item, 1, items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            scratch.Rebind(items[i], i + 1, items.Count);
            foreach (object value in left.Evaluate(scratch))
            {
                if (value is double or bool)
                {
                    return NoIndex;
                }

                string key = ToStringValue(value);
                if (!index.TryGetValue(key, out List<int>? at))
                {
                    index[key] = at = [];
                }

                if (at.Count == 0 || at[^1] != i)
                {
                    at.Add(i);
                }
            }
        }

        return index;
    }

    private static readonly object NoIndex = new();

    // Lists shorter than this are searched, not indexed.
    private const int MinIndexedItems = 16;

    private sealed class IndexEntry(IReadOnlyList<object> list)
    {
        public IReadOnlyList<object> List { get; } = list;

        public object? Index { get; set; }
    }

}
