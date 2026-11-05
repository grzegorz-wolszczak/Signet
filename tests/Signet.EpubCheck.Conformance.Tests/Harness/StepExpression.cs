using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.EpubCheck.Conformance.Tests.Harness;

/// <summary>
/// A Cucumber expression, as upstream's step definitions write them: text with <c>(optional)</c> parts,
/// <c>alternative/words</c>, <c>{parameters}</c> and <c>\</c> escapes. It is turned into a regular expression; the values of
/// the parameters come out converted by their <see cref="ParameterType"/>.
/// </summary>
/// <remarks>
/// Only what upstream's step definitions use is supported; the whole text of a step must match. (Cucumber's own .NET
/// package makes the parameter-type registry the caller's job and is not used here.)
/// </remarks>
internal sealed class StepExpression
{
    private readonly Regex _regex;
    private readonly List<ParameterType> _parameters = [];

    private StepExpression(string source, ParameterTypeRegistry registry)
    {
        Source = source;
        StringBuilder pattern = new("^");
        foreach (Word word in SplitWords(source))
        {
            if (word.IsSpace)
            {
                pattern.Append(Regex.Escape(word.Text));
            }
            else
            {
                pattern.Append(WordPattern(word.Text, registry));
            }
        }

        pattern.Append('$');
        _regex = new Regex(pattern.ToString(), RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);
    }

    public string Source { get; }

    public static StepExpression Parse(string source, ParameterTypeRegistry registry) => new(source, registry);

    /// <summary>Matches the whole <paramref name="text"/>; the converted values of the parameters come out in <paramref name="values"/>.</summary>
    public bool TryMatch(string text, out object?[] values)
    {
        Match match = _regex.Match(text);
        if (!match.Success)
        {
            values = [];
            return false;
        }

        values = new object?[_parameters.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = _parameters[i].Convert(match.Groups["p" + i].Value);
        }

        return true;
    }

    private readonly record struct Word(string Text, bool IsSpace);

    // Splits the expression into runs of white space and "words"; white space inside (...) or after a backslash belongs to the word.
    private static IEnumerable<Word> SplitWords(string source)
    {
        StringBuilder current = new();
        bool currentIsSpace = false;
        int depth = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            bool isSpace;
            if (c == '\\' && i + 1 < source.Length)
            {
                current.Append(c).Append(source[++i]);
                isSpace = false;
            }
            else
            {
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }

                isSpace = depth == 0 && c == ' ';
                if (current.Length > 0 && isSpace != currentIsSpace)
                {
                    yield return new Word(current.ToString(), currentIsSpace);
                    current.Clear();
                }

                current.Append(c);
            }

            currentIsSpace = isSpace;
            if (c == '\\')
            {
                currentIsSpace = false;
            }
        }

        if (current.Length > 0)
        {
            yield return new Word(current.ToString(), currentIsSpace);
        }
    }

    // A word is text, (optional text), {parameters}; a '/' between parts makes them alternatives.
    private string WordPattern(string word, ParameterTypeRegistry registry)
    {
        List<StringBuilder> alternatives = [new StringBuilder()];
        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            StringBuilder current = alternatives[^1];
            switch (c)
            {
                case '\\' when i + 1 < word.Length:
                    current.Append(Regex.Escape(word[++i].ToString()));
                    break;
                case '/':
                    alternatives.Add(new StringBuilder());
                    break;
                case '(':
                    int close = FindClose(word, i);
                    current.Append("(?:").Append(Literal(word[(i + 1)..close])).Append(")?");
                    i = close;
                    break;
                case '{':
                    int end = word.IndexOf('}', i);
                    ParameterType type = registry.Get(word[(i + 1)..end]);
                    current.Append(ParameterPattern(type, _parameters.Count));
                    _parameters.Add(type);
                    i = end;
                    break;
                default:
                    current.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        return alternatives.Count == 1 ? alternatives[0].ToString() : "(?:" + string.Join('|', alternatives.Select(a => a.ToString())) + ")";
    }

    private static int FindClose(string word, int open)
    {
        for (int i = open + 1; i < word.Length; i++)
        {
            if (word[i] == '\\')
            {
                i++;
            }
            else if (word[i] == ')')
            {
                return i;
            }
        }

        throw new StepBindingException($"Unclosed '(' in the expression word '{word}'.");
    }

    private static string Literal(string text)
    {
        StringBuilder result = new();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
            }

            result.Append(Regex.Escape(text[i].ToString()));
        }

        return result.ToString();
    }

    // Upstream's patterns that start with "?i:" are wrapped in a group by Cucumber, which makes them an inline option group.
    private static string ParameterPattern(ParameterType type, int index)
    {
        string inner = type.Pattern.StartsWith("?i:", StringComparison.Ordinal) ? "(" + type.Pattern + ")" : type.Pattern;
        return $"(?<p{index}>{inner})";
    }
}
