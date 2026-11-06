using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.EpubCheck.Core.Util;

/// <summary>What went wrong while a viewport <c>meta</c> value was parsed (upstream's <c>ViewportMeta.ParseError</c>).</summary>
public enum ViewportMetaParseError
{
    /// <summary>The value is <c>null</c> or empty.</summary>
    NullOrEmpty,

    /// <summary>A property has no name.</summary>
    NameEmpty,

    /// <summary>A property has no value.</summary>
    ValueEmpty,

    /// <summary>The value starts with a separator.</summary>
    LeadingSeparator,

    /// <summary>The value ends with a separator.</summary>
    TrailingSeparator,

    /// <summary>An <c>=</c> where a value is expected.</summary>
    AssignUnexpected,
}

/// <summary>
/// A viewport <c>meta</c> tag value parsed with the syntax of EPUB 3 (appendix "Viewport meta tag syntax"; upstream's
/// <c>org.w3c.epubcheck.util.microsyntax.ViewportMeta</c>, a state machine ported state by state): properties
/// <c>name=value</c>, separated by commas, semicolons or white space; a name may repeat.
/// </summary>
public sealed partial class ViewportMeta
{
    private readonly List<KeyValuePair<string, List<string>>> _properties;

    private ViewportMeta(List<KeyValuePair<string, List<string>>> properties)
    {
        _properties = properties;
    }

    private enum State
    {
        Name,
        Assign,
        Value,
        Separator,
        SpaceOrSeparator,
    }

    /// <summary>The names of the properties, in the order they first appear.</summary>
    public IEnumerable<string> Names => _properties.Select(property => property.Key);

    /// <summary>
    /// Parses <paramref name="value"/>. Errors go to <paramref name="errorHandler"/> with the position (in code points,
    /// counted from 1; -1 for an empty value); parsing stops at the first error that is not recoverable.
    /// </summary>
    public static ViewportMeta Parse(string? value, Action<ViewportMetaParseError, int>? errorHandler = null)
    {
        List<KeyValuePair<string, List<string>>> properties = [];
        void Error(ViewportMetaParseError error, int position) => errorHandler?.Invoke(error, position);
        ViewportMeta Build() => new(properties);
        void WithProperty(string name, string propertyValue)
        {
            int index = properties.FindIndex(property => property.Key == name);
            if (index < 0)
            {
                properties.Add(new KeyValuePair<string, List<string>>(name, [propertyValue]));
            }
            else
            {
                properties[index].Value.Add(propertyValue);
            }
        }

        if (string.IsNullOrEmpty(value))
        {
            Error(ViewportMetaParseError.NullOrEmpty, -1);
            return Build();
        }

        int[] input = value.EnumerateRunes().Select(rune => rune.Value).ToArray();
        StringBuilder name = new();
        StringBuilder propertyValue = new();
        State state = State.Name;
        bool consume = true;
        int next = 0;
        int c = 0;
        int position = 0;
        while (!consume || next < input.Length)
        {
            if (consume)
            {
                c = input[next++];
                position++;
            }

            consume = true;
            switch (state)
            {
                case State.Name:
                    if (name.Length == 0 && CodePoints.IsAsciiWhitespace(c))
                    {
                        // leading whitespace
                    }
                    else if (c == '=' || CodePoints.IsAsciiWhitespace(c))
                    {
                        state = State.Assign;
                        consume = false;
                    }
                    else if (c is ',' or ';')
                    {
                        state = State.Separator;
                        consume = false;
                    }
                    else
                    {
                        name.Append(char.ConvertFromUtf32(c));
                    }

                    break;
                case State.Assign:
                    if (name.Length == 0)
                    {
                        Error(ViewportMetaParseError.NameEmpty, position);
                        return Build();
                    }
                    else if (CodePoints.IsAsciiWhitespace(c))
                    {
                        // whitespace
                    }
                    else if (c == '=')
                    {
                        state = State.Value;
                    }
                    else if (c is ',' or ';')
                    {
                        state = State.Separator;
                        consume = false;
                    }
                    else
                    {
                        // no '=' (the property has no value)
                        Error(ViewportMetaParseError.ValueEmpty, position);
                        return Build();
                    }

                    break;
                case State.Value:
                    if (CodePoints.IsAsciiWhitespace(c) && propertyValue.Length == 0)
                    {
                        // whitespace before the value
                    }
                    else if (c is ',' or ';' || CodePoints.IsAsciiWhitespace(c))
                    {
                        if (propertyValue.Length == 0)
                        {
                            Error(ViewportMetaParseError.ValueEmpty, position);
                            return Build();
                        }

                        state = State.SpaceOrSeparator;
                        consume = false;
                    }
                    else if (c == '=')
                    {
                        Error(ViewportMetaParseError.AssignUnexpected, position);
                        return Build();
                    }
                    else
                    {
                        propertyValue.Append(char.ConvertFromUtf32(c));
                    }

                    break;
                case State.SpaceOrSeparator:
                case State.Separator:
                    // SPACE_OR_SEPARATOR falls through to SEPARATOR in upstream, also when it has just moved to it.
                    if (state == State.SpaceOrSeparator && !CodePoints.IsAsciiWhitespace(c))
                    {
                        state = State.Separator;
                        consume = false;
                    }

                    if (name.Length == 0)
                    {
                        Error(ViewportMetaParseError.LeadingSeparator, position);
                        return Build();
                    }

                    if (c is ',' or ';' || CodePoints.IsAsciiWhitespace(c))
                    {
                        // repeated separators
                    }
                    else
                    {
                        WithProperty(name.ToString(), propertyValue.ToString());
                        name.Clear();
                        propertyValue.Clear();
                        state = State.Name;
                        consume = false;
                    }

                    break;
            }
        }

        if (state == State.Value && propertyValue.Length == 0)
        {
            Error(ViewportMetaParseError.ValueEmpty, position);
        }
        else
        {
            WithProperty(name.ToString(), propertyValue.ToString());
        }

        if (state == State.Separator)
        {
            Error(ViewportMetaParseError.TrailingSeparator, position);
        }

        return Build();
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a valid value of the property <paramref name="name"/>: a number or
    /// <c>device-width</c> for <c>width</c>, a number or <c>device-height</c> for <c>height</c>; anything for the others.
    /// </summary>
    public static bool IsValidProperty(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        return name switch
        {
            "width" => WidthPattern().IsMatch(value),
            "height" => HeightPattern().IsMatch(value),
            _ => true,
        };
    }

    /// <summary>Whether the value has a property <paramref name="name"/>.</summary>
    public bool HasProperty(string name) => _properties.Exists(property => property.Key == name);

    /// <summary>The values of the property <paramref name="name"/>, in their order; empty when there is none.</summary>
    public IReadOnlyList<string> GetValues(string name) =>
        _properties.FirstOrDefault(property => property.Key == name).Value?.AsReadOnly() ?? (IReadOnlyList<string>)[];

    // Upstream's patterns, matched against the whole value; \d is ASCII in Java.
    [GeneratedRegex(@"\A(?:[0-9]+(\.[0-9]+)?|device-width)\z", RegexOptions.CultureInvariant)]
    private static partial Regex WidthPattern();

    [GeneratedRegex(@"\A(?:[0-9]+(\.[0-9]+)?|device-height)\z", RegexOptions.CultureInvariant)]
    private static partial Regex HeightPattern();
}
