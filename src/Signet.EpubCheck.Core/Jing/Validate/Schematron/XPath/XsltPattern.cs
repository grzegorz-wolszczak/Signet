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
    private readonly List<XPathExpr> _alternatives = [];

    public XsltPattern(XPathExpr expr)
    {
        Collect(expr);
    }

    public bool Matches(XdmNode node, XPathContext context) => _alternatives.Exists(a => MatchesAlternative(a, node, context));

    private void Collect(XPathExpr expr)
    {
        if (expr is SetExpr set && set.Op == "union")
        {
            Collect(set.Left);
            Collect(set.Right);
        }
        else
        {
            _alternatives.Add(expr is ParenthesizedExpr p ? p.Inner : expr);
        }
    }

    private static bool MatchesAlternative(XPathExpr alternative, XdmNode node, XPathContext context)
    {
        (bool absolute, IReadOnlyList<XPathExpr> steps) = alternative switch
        {
            PathExpr path => (path.Absolute, path.Steps),
            AxisStep step => (false, [step]),
            _ => (false, (IReadOnlyList<XPathExpr>)[]),
        };
        if (steps.Count == 0 && alternative is PathExpr { Absolute: true })
        {
            return node.Kind == XdmNodeKind.Document;
        }

        if (steps.Count == 0 || steps.Any(s => s is not AxisStep))
        {
            // Not a path of steps: the node matches if the expression, evaluated at the root, selects it.
            return alternative.Evaluate(context.With(node.Root, 1, 1)).Any(i => ReferenceEquals(i, node));
        }

        return MatchFrom(node, steps.Count - 1, absolute, steps, context);
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

        XPathContext nodeContext = context.With(node, 1, 1);
        foreach (XPathExpr predicate in step.Predicates)
        {
            if (predicate.UsesPosition)
            {
                return step.Evaluate(context.With(parent, 1, 1)).Any(n => ReferenceEquals(n, node));
            }

            IReadOnlyList<object> value = predicate.Evaluate(nodeContext);
            if (value.Count == 1 && value[0] is double)
            {
                return step.Evaluate(context.With(parent, 1, 1)).Any(n => ReferenceEquals(n, node));
            }

            if (!XPathValues.EffectiveBoolean(value))
            {
                return false;
            }
        }

        return true;
    }
}
