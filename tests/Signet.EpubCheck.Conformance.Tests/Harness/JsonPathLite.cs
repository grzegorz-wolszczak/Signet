using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>What a JSONPath finds: a single value for a path that names one place, a list for a path that can match many.</summary>
internal sealed record JsonPathResult(bool IsDefinite, IReadOnlyList<JsonNode?> Nodes)
{
    /// <summary>The value of a definite path (and of an indefinite one that matched exactly once).</summary>
    public JsonNode? Single => Nodes.Count == 1
        ? Nodes[0]
        : throw new InvalidOperationException($"The JSON path matched {Nodes.Count} values, one was expected.");

    /// <summary>The values the test looks at: the elements of an array a definite path names, or everything an indefinite one matched.</summary>
    public IReadOnlyList<JsonNode?> Items => IsDefinite && Nodes.Count == 1 && Nodes[0] is JsonArray array ? array.ToList() : Nodes;
}

/// <summary>
/// The part of JSONPath the scenarios of upstream use: <c>$</c>, <c>.name</c>, <c>..name</c>, <c>[n]</c>, <c>[*]</c> and
/// <c>[?(@.name==value)]</c> with a string, a boolean or a number.
/// </summary>
internal static class JsonPathLite
{
    public static JsonPathResult Evaluate(JsonNode root, string path)
    {
        if (!path.StartsWith('$'))
        {
            throw new ArgumentException("A JSON path starts with $: " + path, nameof(path));
        }

        List<JsonNode?> current = [root];
        bool definite = true;
        int i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                bool descend = i + 1 < path.Length && path[i + 1] == '.';
                i += descend ? 2 : 1;
                int start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[')
                {
                    i++;
                }

                string name = path[start..i];
                if (descend)
                {
                    definite = false;
                    current = Descend(current, name);
                }
                else if (name == "*")
                {
                    definite = false;
                    current = Children(current);
                }
                else
                {
                    current = Child(current, name);
                }
            }
            else if (path[i] == '[')
            {
                int end = ClosingBracket(path, i);
                string inner = path[(i + 1)..end];
                i = end + 1;
                if (inner == "*")
                {
                    definite = false;
                    current = Children(current);
                }
                else if (inner.StartsWith("?(", StringComparison.Ordinal) && inner.EndsWith(')'))
                {
                    definite = false;
                    current = Filter(current, inner[2..^1]);
                }
                else if (int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    current = current.Select(n => n is JsonArray a && index < a.Count ? a[index] : null).Where(n => n is not null).ToList();
                }
                else
                {
                    current = Child(current, inner.Trim('\'', '"'));
                }
            }
            else
            {
                throw new FormatException($"Unexpected character '{path[i]}' in the JSON path {path}");
            }
        }

        return new JsonPathResult(definite, current);
    }

    private static int ClosingBracket(string path, int open)
    {
        int depth = 0;
        for (int i = open; i < path.Length; i++)
        {
            if (path[i] == '[')
            {
                depth++;
            }
            else if (path[i] == ']' && --depth == 0)
            {
                return i;
            }
        }

        throw new FormatException("Unclosed [ in the JSON path " + path);
    }

    private static List<JsonNode?> Child(List<JsonNode?> nodes, string name) =>
        nodes.Select(n => n is JsonObject o && o.ContainsKey(name) ? new Found(o[name]) : null).Where(f => f is not null).Select(f => f!.Node).ToList();

    private static List<JsonNode?> Children(List<JsonNode?> nodes)
    {
        List<JsonNode?> result = [];
        foreach (JsonNode? node in nodes)
        {
            if (node is JsonArray array)
            {
                result.AddRange(array);
            }
            else if (node is JsonObject obj)
            {
                result.AddRange(obj.Select(p => p.Value));
            }
        }

        return result;
    }

    private static List<JsonNode?> Descend(List<JsonNode?> nodes, string name)
    {
        List<JsonNode?> result = [];
        foreach (JsonNode? node in nodes)
        {
            Collect(node, name, result);
        }

        return result;
    }

    // Everything under the node (and the node), in document order, that is the value of a property called name.
    private static void Collect(JsonNode? node, string name, List<JsonNode?> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    if (property.Key == name)
                    {
                        result.Add(property.Value);
                    }
                }

                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    Collect(property.Value, name, result);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? element in array)
                {
                    Collect(element, name, result);
                }

                break;
        }
    }

    private static List<JsonNode?> Filter(List<JsonNode?> nodes, string expression)
    {
        // @.name==value
        int equals = expression.IndexOf("==", StringComparison.Ordinal);
        if (!expression.StartsWith("@.", StringComparison.Ordinal) || equals < 0)
        {
            throw new FormatException("Unsupported JSON path filter: " + expression);
        }

        string name = expression[2..equals];
        string literal = expression[(equals + 2)..].Trim();
        List<JsonNode?> result = [];
        foreach (JsonNode? candidate in Children(nodes))
        {
            if (candidate is JsonObject obj && obj.ContainsKey(name) && Matches(obj[name], literal))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static bool Matches(JsonNode? value, string literal)
    {
        if (value is null)
        {
            return literal == "null";
        }

        if (literal.Length >= 2 && (literal[0] == '"' || literal[0] == '\'') && literal[^1] == literal[0])
        {
            return value is JsonValue v && v.TryGetValue(out string? s) && s == literal[1..^1];
        }

        if (literal is "true" or "false")
        {
            return value is JsonValue b && b.TryGetValue(out bool flag) && flag == (literal == "true");
        }

        return value is JsonValue n && n.TryGetValue(out double number)
            && double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double expected) && number == expected;
    }

    private sealed record Found(JsonNode? Node);
}
