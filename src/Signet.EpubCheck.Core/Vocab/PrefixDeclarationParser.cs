using System;
using System.Collections.Generic;
using System.Xml;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Vocab;

/// <summary>
/// Parses the value of a <c>prefix</c> attribute, <c>prefix: URI</c> pairs separated by white space (upstream's
/// <c>PrefixDeclarationParser</c>, a state machine ported state by state). Upstream reads with a <c>Reader</c> and
/// <c>mark</c>/<c>reset</c>: each state takes the longest run of the characters it accepts, and the first character it
/// does not accept is read again by the next state. Here an index does the same.
/// </summary>
public static class PrefixDeclarationParser
{
    private enum State
    {
        Start,
        Prefix,
        PrefixEnd,
        Space,
        Uri,
        Whitespace,
    }

    /// <summary>
    /// The prefix-to-URI mappings of <paramref name="value"/>, in their order; the problems are reported at
    /// <paramref name="location"/> (<see cref="MessageId.OPF_004"/> to <see cref="MessageId.OPF_006"/>). A prefix declared
    /// twice keeps its first URI.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ParsePrefixMappings(string? value, IReport report, EpubLocation location)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(location);
        List<KeyValuePair<string, string>> mappings = [];
        if (value is null)
        {
            return mappings;
        }

        string? prefix = null;
        State state = State.Start;
        string chars = string.Empty;
        int position = 0;
        while (position < value.Length)
        {
            (chars, string badChars) = Consume(value, ref position, state);
            switch (state)
            {
                case State.Start:
                    prefix = null;
                    if (chars.Length != 0)
                    {
                        report.Message(MessageId.OPF_004, location);
                    }

                    state = State.Prefix;
                    break;
                case State.Prefix:
                    if (chars.Length == 0)
                    {
                        report.Message(MessageId.OPF_004a, location);
                    }
                    else if (!IsNcName(chars))
                    {
                        report.Message(MessageId.OPF_004b, location, chars);
                    }
                    else
                    {
                        prefix = chars;
                    }

                    state = State.PrefixEnd;
                    break;
                case State.PrefixEnd:
                    if (chars.Length == 0)
                    {
                        // No colon after the prefix: skip the white space to see what follows.
                        bool colonFollows = SkipWhitespace(value, ref position) == ':';
                        report.Message(MessageId.OPF_004c, location, prefix);
                        state = colonFollows ? State.PrefixEnd : State.Uri;
                        prefix = null;
                        break;
                    }

                    state = State.Space;
                    break;
                case State.Space:
                    if (chars.Length == 0)
                    {
                        report.Message(MessageId.OPF_004d, location, prefix);
                        prefix = null;
                    }
                    else if (badChars.Length != 0)
                    {
                        report.Message(MessageId.OPF_004e, location, prefix);
                    }

                    state = State.Uri;
                    break;
                case State.Uri:
                    if (JavaUri.IsValid(chars))
                    {
                        if (prefix is not null && !mappings.Exists(mapping => mapping.Key == prefix))
                        {
                            mappings.Add(new KeyValuePair<string, string>(prefix, chars));
                        }
                    }
                    else
                    {
                        report.Message(MessageId.OPF_006, location, chars, prefix);
                    }

                    prefix = null;
                    state = State.Whitespace;
                    break;
                case State.Whitespace:
                    if (badChars.Length != 0)
                    {
                        report.Message(MessageId.OPF_004f, location, prefix);
                    }

                    state = State.Prefix;
                    break;
            }
        }

        // The value ends with a prefix and no URI.
        if (state is not (State.Start or State.Prefix or State.Whitespace))
        {
            report.Message(MessageId.OPF_005, location, prefix);
        }

        // Trailing white space.
        if (state == State.Prefix && chars.Length != 0)
        {
            report.Message(MessageId.OPF_004, location);
        }

        return mappings;
    }

    // The longest run from position of the characters the state accepts, and those of them it does not allow.
    private static (string Chars, string BadChars) Consume(string value, ref int position, State state)
    {
        int start = position;
        System.Text.StringBuilder bad = new();
        while (position < value.Length && Accepts(state, value[position]))
        {
            if (!Allows(state, value[position]))
            {
                bad.Append(value[position]);
            }

            position++;
        }

        return (value[start..position], bad.ToString());
    }

    // Skips white space up to a colon; returns the character there (or '\0' at the end), not consumed.
    private static char SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && value[position] != ':' && IsWhitespace(value[position]))
        {
            position++;
        }

        return position < value.Length ? value[position] : '\0';
    }

    private static bool Accepts(State state, char c) => state switch
    {
        State.Start or State.Space or State.Whitespace => IsWhitespace(c),
        State.Prefix => !IsWhitespace(c) && c != ':',
        State.PrefixEnd => c == ':',
        State.Uri => !IsWhitespace(c),
        _ => false,
    };

    private static bool Allows(State state, char c) => state switch
    {
        State.Start => false,
        State.Space => c == ' ',
        State.Whitespace => c is ' ' or '\t' or '\r' or '\n',
        _ => true,
    };

    // Guava's CharMatcher.whitespace(): the Unicode White_Space characters, as char.IsWhiteSpace.
    private static bool IsWhitespace(char c) => char.IsWhiteSpace(c);

    private static bool IsNcName(string value)
    {
        try
        {
            XmlConvert.VerifyNCName(value);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
