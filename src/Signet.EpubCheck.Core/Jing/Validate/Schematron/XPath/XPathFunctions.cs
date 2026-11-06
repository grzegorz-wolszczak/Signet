using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Jing.Validate.Schematron.XPath;

/// <summary>
/// The functions of XPath 2.0 (and XSLT's <c>current()</c>) the Schematron schemas use. In XPath 1.0 compatibility
/// mode a function that takes a single value gets the first item of a sequence. Regular expressions (<c>matches</c>,
/// <c>tokenize</c>, <c>replace</c>) are run by .NET's engine.
/// </summary>
internal static class XPathFunctions
{
    private static readonly TimeSpan s_regexTimeout = Regex.InfiniteMatchTimeout;

    public static IReadOnlyList<object> Call(string name, IReadOnlyList<XPathExpr> args, XPathContext context)
    {
        // Strip the standard function namespace prefix if it is written.
        string local = name.StartsWith("fn:", StringComparison.Ordinal) ? name[3..] : name;
        IReadOnlyList<object> Arg(int i) => args[i].Evaluate(context);
        string StringArg(int i) => XPathValues.StringOfFirst(Arg(i));
        string ContextString() => args.Count == 0 ? XPathValues.ToStringValue(context.Item) : StringArg(0);
        XdmNode? NodeArg() => args.Count == 0 ? context.ContextNode : Arg(0).FirstOrDefault() as XdmNode;

        switch (local)
        {
            case "true":
                return [true];
            case "false":
                return [false];
            case "not":
                return [!XPathValues.EffectiveBoolean(Arg(0))];
            case "boolean":
                return [XPathValues.EffectiveBoolean(Arg(0))];
            case "exists":
                return [Arg(0).Count > 0];
            case "empty":
                return [Arg(0).Count == 0];
            case "count":
                return [(double)Arg(0).Count];
            case "position":
                return [(double)context.Position];
            case "last":
                return [(double)context.Size];
            case "current":
                return [context.Current];
            case "string":
                return [ContextString()];
            case "number":
                return [args.Count == 0 ? XPathValues.ToNumber(context.Item) : XPathValues.NumberOfFirst(Arg(0))];
            case "normalize-space":
                return [string.Join(' ', ContextString().Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))];
            case "string-length":
                string sl = ContextString();
                return [(double)(sl.Length - sl.Count(char.IsHighSurrogate))];
            case "lower-case":
                return [StringArg(0).ToLowerInvariant()];
            case "upper-case":
                return [StringArg(0).ToUpperInvariant()];
            case "concat":
                return [string.Concat(args.Select((_, i) => StringArg(i)))];
            case "contains":
                return [StringArg(0).Contains(StringArg(1), StringComparison.Ordinal)];
            case "starts-with":
                return [StringArg(0).StartsWith(StringArg(1), StringComparison.Ordinal)];
            case "ends-with":
                return [StringArg(0).EndsWith(StringArg(1), StringComparison.Ordinal)];
            case "substring-before":
                string sb0 = StringArg(0);
                int ib = sb0.IndexOf(StringArg(1), StringComparison.Ordinal);
                return [ib < 0 ? string.Empty : sb0[..ib]];
            case "substring-after":
                string sa0 = StringArg(0);
                string sa1 = StringArg(1);
                int ia = sa0.IndexOf(sa1, StringComparison.Ordinal);
                return [ia < 0 ? string.Empty : sa0[(ia + sa1.Length)..]];
            case "substring":
                return [Substring(StringArg(0), XPathValues.NumberOfFirst(Arg(1)), args.Count > 2 ? XPathValues.NumberOfFirst(Arg(2)) : double.PositiveInfinity)];
            case "translate":
                return [Translate(StringArg(0), StringArg(1), StringArg(2))];
            case "string-join":
                string separator = args.Count > 1 ? StringArg(1) : string.Empty;
                return [string.Join(separator, XPathValues.Atomize(Arg(0)).Select(XPathValues.ToStringValue))];
            case "tokenize":
                string input = StringArg(0);
                if (input.Length == 0)
                {
                    return [];
                }

                return [.. MakeRegex(StringArg(1), args.Count > 2 ? StringArg(2) : string.Empty).Split(input)];
            case "matches":
                return [MakeRegex(StringArg(1), args.Count > 2 ? StringArg(2) : string.Empty).IsMatch(StringArg(0))];
            case "replace":
                return [MakeRegex(StringArg(1), args.Count > 3 ? StringArg(3) : string.Empty).Replace(StringArg(0), StringArg(2))];
            case "name":
                return [NodeArg()?.QName ?? string.Empty];
            case "local-name":
                return [NodeArg()?.LocalName ?? string.Empty];
            case "namespace-uri":
                return [NodeArg()?.NamespaceUri ?? string.Empty];
            case "distinct-values":
                List<object> distinct = [];
                foreach (object item in XPathValues.Atomize(Arg(0)).Select(i => i is UntypedAtomic u ? u.Value : i))
                {
                    if (!distinct.Exists(d => XPathValues.Test("=", XPathValues.CompareAtomic(d, item), d, item)))
                    {
                        distinct.Add(item);
                    }
                }

                return distinct;
            case "sum":
                return [XPathValues.Atomize(Arg(0)).Sum(XPathValues.ToNumber)];
            case "floor":
                return [Math.Floor(XPathValues.NumberOfFirst(Arg(0)))];
            case "ceiling":
                return [Math.Ceiling(XPathValues.NumberOfFirst(Arg(0)))];
            case "round":
                return [Math.Floor(XPathValues.NumberOfFirst(Arg(0)) + 0.5)];
            case "abs":
                return [Math.Abs(XPathValues.NumberOfFirst(Arg(0)))];
            case "resolve-uri":
                IReadOnlyList<object> relative = Arg(0);
                if (relative.Count == 0)
                {
                    return [];
                }

                string baseUri = args.Count > 1 ? StringArg(1) : context.BaseUri ?? string.Empty;
                string rel = XPathValues.StringOfFirst(relative);
                return [baseUri.Length == 0 || JingUri.IsAbsolute(rel) ? rel : JavaUri.Resolve(baseUri, rel)];
            default:
                throw new XPathException("unknown function \"" + name + "\"");
        }
    }

    // XPath's substring: positions round half up, counted from 1.
    private static string Substring(string s, double start, double length)
    {
        double first = Math.Floor(start + 0.5);
        double last = first + Math.Floor(length + 0.5);
        StringBuilder sb = new();
        for (int i = 0; i < s.Length; i++)
        {
            double p = i + 1;
            if (p >= first && p < last)
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }

    private static string Translate(string s, string from, string to)
    {
        StringBuilder sb = new();
        foreach (char c in s)
        {
            int i = from.IndexOf(c, StringComparison.Ordinal);
            if (i < 0)
            {
                sb.Append(c);
            }
            else if (i < to.Length)
            {
                sb.Append(to[i]);
            }
        }

        return sb.ToString();
    }

    private static Regex MakeRegex(string pattern, string flags)
    {
        RegexOptions options = RegexOptions.CultureInvariant;
        foreach (char f in flags)
        {
            options |= f switch
            {
                'i' => RegexOptions.IgnoreCase,
                's' => RegexOptions.Singleline,
                'm' => RegexOptions.Multiline,
                'x' => RegexOptions.IgnorePatternWhitespace,
                _ => throw new XPathException("invalid regular expression flag \"" + f + "\""),
            };
        }

        try
        {
            return new Regex(pattern, options, s_regexTimeout);
        }
        catch (ArgumentException e)
        {
            throw new XPathException("invalid regular expression \"" + pattern + "\": " + e.Message, e);
        }
    }
}
