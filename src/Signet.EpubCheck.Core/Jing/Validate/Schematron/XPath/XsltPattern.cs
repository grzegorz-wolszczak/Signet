using System.Collections.Generic;
using System.Linq;

namespace Signet.EpubCheck.Core.Jing.Validate.Schematron.XPath;

/// <summary>
/// An XSLT match pattern (the <c>context</c> of a rule): a node matches when one of the paths of the union would select
/// it from some context. A path of child and attribute steps is matched from its last step back to the root, a step's
/// predicates evaluated from the node's parent (positional predicates against the parent's children).
/// </summary>
internal sealed class XsltPattern
{
    private readonly List<Alternative> _alternatives = [];

    public XsltPattern(XPathExpr expr)
    {
        Collect(expr);
    }

    /// <summary>
    /// The (namespace, local name) of every element this pattern can match, or <c>null</c> when it can match elements of any name (or
    /// something that is not an element path). A node whose name is not among them is not matched: no need to try.
    /// </summary>
    public IReadOnlyList<(string Namespace, string LocalName)>? ElementNames
    {
        get
        {
            List<(string, string)> names = [];
            foreach (Alternative alternative in _alternatives)
            {
                if (!alternative.AllAxisSteps
                    || alternative.Steps[^1] is not AxisStep { Axis: Axis.Child, Test: NameTest { NamespaceUri: { } ns, LocalName: { } local } })
                {
                    return null;
                }

                names.Add((ns, local));
            }

            return names;
        }
    }

    public bool Matches(XdmNode node, XPathContext context)
    {
        for (int i = 0; i < _alternatives.Count; i++)
        {
            if (MatchesAlternative(_alternatives[i], node, context))
            {
                return true;
            }
        }

        return false;
    }

    private void Collect(XPathExpr expr)
    {
        if (expr is SetExpr set && set.Op == "union")
        {
            Collect(set.Left);
            Collect(set.Right);
        }
        else
        {
            _alternatives.Add(new Alternative(expr is ParenthesizedExpr p ? p.Inner : expr));
        }
    }

    // What matching needs to know of an alternative, worked out once and not for every node.
    private sealed class Alternative
    {
        public Alternative(XPathExpr expr)
        {
            Expr = expr;
            switch (expr)
            {
                case PathExpr path:
                    Absolute = path.Absolute;
                    Steps = path.Steps;
                    break;
                case AxisStep step:
                    Steps = [step];
                    break;
                default:
                    Steps = [];
                    break;
            }

            IsRoot = Steps.Count == 0 && expr is PathExpr { Absolute: true };
            AllAxisSteps = Steps.Count > 0 && Steps.All(s => s is AxisStep);
        }

        public XPathExpr Expr { get; }

        public bool Absolute { get; }

        public IReadOnlyList<XPathExpr> Steps { get; }

        /// <summary>The pattern "/": the document node.</summary>
        public bool IsRoot { get; }

        public bool AllAxisSteps { get; }
    }

    private static bool MatchesAlternative(Alternative alternative, XdmNode node, XPathContext context)
    {
        if (alternative.IsRoot)
        {
            return node.Kind == XdmNodeKind.Document;
        }

        // Not a path of steps: the node matches if the expression, evaluated at the root, selects it.
        return alternative.AllAxisSteps
            ? MatchFrom(node, alternative.Steps.Count - 1, alternative.Absolute, alternative.Steps, context)
            : Selected(alternative.Expr, node.Root, node, context);
    }

    // Kept out of the matching methods: a lambda that captures the node would make the compiler allocate its closure on every call of them.
    private static bool Selected(XPathExpr expr, XdmNode from, XdmNode node, XPathContext context)
    {
        IReadOnlyList<object> items = expr.Evaluate(context.With(from, 1, 1));
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], node))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchFrom(XdmNode node, int i, bool absolute, IReadOnlyList<XPathExpr> steps, XPathContext context)
    {
        AxisStep step = (AxisStep)steps[i];
        XdmNode? parent = node.Parent;
        if (parent is null || !Selects(step, parent, node, context))
        {
            return false;
        }

        if (i == 0)
        {
            return !absolute || parent.Kind == XdmNodeKind.Document;
        }

        AxisStep previous = (AxisStep)steps[i - 1];
        if (previous.Axis == Axis.DescendantOrSelf && previous.Test is KindTest && previous.Predicates.Count == 0)
        {
            if (i - 1 == 0)
            {
                return true;
            }

            for (XdmNode? a = parent; a is not null; a = a.Parent)
            {
                if (MatchFrom(a, i - 2, absolute, steps, context))
                {
                    return true;
                }
            }

            return false;
        }

        return MatchFrom(parent, i - 1, absolute, steps, context);
    }

    // Whether the step, from the parent, selects the node; a predicate that needs positions makes it evaluate the step.
    private static bool Selects(AxisStep step, XdmNode parent, XdmNode node, XPathContext context)
    {
        if (step.Axis is not (Axis.Child or Axis.Attribute) || !step.Test.Matches(node, step.Axis)
            || (step.Axis == Axis.Child) == (node.Kind == XdmNodeKind.Attribute))
        {
            return false;
        }

        if (step.Predicates.Count == 0)
        {
            return true;
        }

        XPathContext nodeContext = context.With(node, 1, 1);
        foreach (XPathExpr predicate in step.Predicates)
        {
            if (predicate.UsesPosition)
            {
                return Selected(step, parent, node, context);
            }

            if (predicate.IsBoolean)
            {
                if (!predicate.EvaluateBoolean(nodeContext))
                {
                    return false;
                }

                continue;
            }

            IReadOnlyList<object> value = predicate.Evaluate(nodeContext);
            if (value.Count == 1 && value[0] is double)
            {
                return Selected(step, parent, node, context);
            }

            if (!XPathValues.EffectiveBoolean(value))
            {
                return false;
            }
        }

        return true;
    }
}
