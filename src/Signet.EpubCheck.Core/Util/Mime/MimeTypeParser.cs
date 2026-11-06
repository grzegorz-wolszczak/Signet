using System.Collections.Generic;
using System.Text;

namespace Signet.EpubCheck.Core.Util.Mime;

/// <summary>What went wrong while a MIME type was parsed (upstream's <c>MIMETypeParser.ParseError</c>).</summary>
public enum MimeTypeParseError
{
    /// <summary>The string is <c>null</c>.</summary>
    Null,

    /// <summary>There is no type.</summary>
    NoType,

    /// <summary>There is no subtype.</summary>
    NoSubtype,

    /// <summary>A code point that ends the parsing.</summary>
    IllegalCodePoint,

    /// <summary>A code point that is skipped.</summary>
    UnexpectedCodePoint,

    /// <summary>A parameter has no name.</summary>
    NoParamName,

    /// <summary>A parameter has no value.</summary>
    NoParamValue,

    /// <summary>A quoted parameter value is not closed.</summary>
    EndQuoteMissing,
}

/// <summary>Receives the problems found by <see cref="MimeTypeParser"/>; the positions count code points from 0.</summary>
public interface IMimeTypeErrorHandler
{
    /// <summary>A problem after which there is no MIME type.</summary>
    void OnFailure(MimeTypeParseError parseError, int position);

    /// <summary>A problem the parser recovers from.</summary>
    void OnError(MimeTypeParseError parseError, int position);
}

/// <summary>
/// Parses a MIME type as the WHATWG MIME Sniffing standard does, reporting what it recovers from (upstream's
/// <c>MIMETypeParser</c>, a state machine ported state by state).
/// </summary>
public sealed class MimeTypeParser
{
    private readonly IMimeTypeErrorHandler? _errorHandler;

    /// <summary>A parser that reports to <paramref name="errorHandler"/> (or to nobody).</summary>
    public MimeTypeParser(IMimeTypeErrorHandler? errorHandler = null)
    {
        _errorHandler = errorHandler;
    }

    private enum State
    {
        Type,
        Subtype,
        SubtypeFinalize,
        ParamName,
        ParamValue,
        ParamValueFinalize,
        ParamValueQuoted,
        ParamValueQuotedEscape,
        ParamValueQuotedFinalize,
        ParamFinalize,
        SkipParam,
    }

    /// <summary>Parses <paramref name="value"/>; <c>null</c> when it is not a MIME type.</summary>
    public MimeType? Parse(string? value)
    {
        if (value is null)
        {
            Failure(MimeTypeParseError.Null, -1);
            return null;
        }

        if (value.Length == 0)
        {
            Failure(MimeTypeParseError.NoType, -1);
            return null;
        }

        MimeType.Builder builder = new();
        List<int> input = [];
        foreach (Rune rune in value.EnumerateRunes())
        {
            input.Add(rune.Value);
        }

        StringBuilder type = new();
        StringBuilder? subtype = null;
        StringBuilder pname = new();
        StringBuilder pvalue = new();
        State state = State.Type;
        bool consume = true;
        int next = 0;
        int c = 0;
        int position = 0;
        int wsstart = -1;
        while (!consume || next < input.Count)
        {
            if (consume)
            {
                c = input[next++];
            }

            consume = true;
            switch (state)
            {
                case State.Type:
                    if (type.Length == 0 && CodePoints.IsHttpWhitespace(c))
                    {
                        // leading whitespace
                    }
                    else if (CodePoints.IsHttpTokenCodePoint(c))
                    {
                        type.Append(char.ToLowerInvariant((char)c));
                    }
                    else if (c == '/')
                    {
                        if (type.Length == 0)
                        {
                            Failure(MimeTypeParseError.NoType, position);
                            return null;
                        }

                        state = State.Subtype;
                        subtype = new StringBuilder();
                    }
                    else
                    {
                        Failure(MimeTypeParseError.IllegalCodePoint, position);
                        return null;
                    }

                    break;
                case State.Subtype:
                    if (subtype!.Length == 0 && CodePoints.IsHttpWhitespace(c))
                    {
                        Failure(MimeTypeParseError.IllegalCodePoint, position);
                        return null;
                    }

                    if (CodePoints.IsHttpTokenCodePoint(c))
                    {
                        subtype.Append(char.ToLowerInvariant((char)c));
                    }
                    else
                    {
                        // ';', whitespace or an invalid code point
                        state = State.SubtypeFinalize;
                        consume = false;
                    }

                    break;
                case State.SubtypeFinalize:
                    if (CodePoints.IsHttpWhitespace(c))
                    {
                        // trailing whitespace
                    }
                    else if (c == ';')
                    {
                        if (subtype!.Length == 0)
                        {
                            Failure(MimeTypeParseError.NoSubtype, position);
                            return null;
                        }

                        state = State.ParamName;
                        pname = new StringBuilder();
                    }
                    else
                    {
                        Failure(MimeTypeParseError.IllegalCodePoint, position);
                        return null;
                    }

                    break;
                case State.ParamName:
                    if (pname.Length == 0 && CodePoints.IsHttpWhitespace(c))
                    {
                        // leading whitespace
                    }
                    else if (CodePoints.IsHttpTokenCodePoint(c))
                    {
                        pname.Append(char.ToLowerInvariant((char)c));
                    }
                    else if (c == ';')
                    {
                        Error(pname.Length == 0 ? MimeTypeParseError.NoParamName : MimeTypeParseError.NoParamValue, position);
                        state = State.ParamName;
                        pname = new StringBuilder();
                    }
                    else if (c != '=')
                    {
                        Error(MimeTypeParseError.UnexpectedCodePoint, position);
                        state = State.SkipParam;
                    }
                    else if (pname.Length == 0)
                    {
                        Error(MimeTypeParseError.NoParamName, position - 1);
                        state = State.SkipParam;
                    }
                    else
                    {
                        state = State.ParamValue;
                        pvalue = new StringBuilder();
                    }

                    break;
                case State.ParamValue:
                    if (pvalue.Length == 0 && c == '"')
                    {
                        state = State.ParamValueQuoted;
                    }
                    else if (CodePoints.IsHttpTokenCodePoint(c))
                    {
                        pvalue.Append((char)c);
                        if (wsstart >= 0)
                        {
                            Error(MimeTypeParseError.UnexpectedCodePoint, wsstart);
                            wsstart = -1;
                        }
                    }
                    else if (CodePoints.IsHttpWhitespace(c))
                    {
                        if (pvalue.Length == 0)
                        {
                            Error(MimeTypeParseError.UnexpectedCodePoint, position);
                        }

                        pvalue.Append((char)c);
                        wsstart = wsstart < 0 ? position : wsstart;
                    }
                    else if (c != ';' && CodePoints.IsHttpQuotedStringTokenCodePoint(c))
                    {
                        Error(MimeTypeParseError.UnexpectedCodePoint, position);
                        pvalue.Append((char)c);
                        wsstart = -1;
                    }
                    else
                    {
                        state = State.ParamValueFinalize;
                        consume = false;
                    }

                    break;
                case State.ParamValueFinalize:
                    if (c == ';' && pvalue.Length == 0)
                    {
                        Error(MimeTypeParseError.NoParamValue, position);
                        state = State.ParamName;
                        pname = new StringBuilder();
                    }
                    else if (c == ';')
                    {
                        TrimTrailingWhitespace(pvalue, position, wsstart);
                        state = State.ParamFinalize;
                        consume = false;
                    }
                    else
                    {
                        Error(MimeTypeParseError.UnexpectedCodePoint, position);
                        state = State.SkipParam;
                    }

                    wsstart = -1;
                    break;
                case State.ParamValueQuoted:
                    if (c == '"')
                    {
                        state = State.ParamValueQuotedFinalize;
                    }
                    else if (c == '\\')
                    {
                        if (next >= input.Count)
                        {
                            pvalue.Append('\\');
                        }

                        state = State.ParamValueQuotedEscape;
                    }
                    else if (CodePoints.IsHttpQuotedStringTokenCodePoint(c))
                    {
                        pvalue.Append((char)c);
                    }
                    else
                    {
                        Error(MimeTypeParseError.UnexpectedCodePoint, position);
                        state = State.SkipParam;
                    }

                    break;
                case State.ParamValueQuotedEscape:
                    if (CodePoints.IsHttpQuotedStringTokenCodePoint(c))
                    {
                        pvalue.Append((char)c);
                        state = State.ParamValueQuoted;
                    }
                    else
                    {
                        Error(MimeTypeParseError.UnexpectedCodePoint, position);
                        state = State.SkipParam;
                    }

                    break;
                case State.ParamValueQuotedFinalize:
                    if (c == ';')
                    {
                        state = State.ParamFinalize;
                        consume = false;
                    }
                    else if (!CodePoints.IsHttpWhitespace(c))
                    {
                        Error(MimeTypeParseError.UnexpectedCodePoint, position);
                    }

                    break;
                case State.ParamFinalize:
                    builder.WithParameter(pname.ToString(), pvalue.ToString());
                    state = State.ParamName;
                    pname = new StringBuilder();
                    break;
                case State.SkipParam:
                    if (pname.Length > 0)
                    {
                        pname = new StringBuilder();
                    }

                    if (c == ';')
                    {
                        state = State.ParamName;
                    }

                    break;
            }

            if (consume)
            {
                position++;
            }
        }

        if (type.Length == 0)
        {
            Failure(MimeTypeParseError.NoType, position);
            return null;
        }

        if (subtype is null || subtype.Length == 0)
        {
            Failure(MimeTypeParseError.NoSubtype, position);
            return null;
        }

        switch (state)
        {
            case State.ParamName:
                Error(pname.Length == 0 ? MimeTypeParseError.NoParamName : MimeTypeParseError.NoParamValue, position);
                break;
            case State.ParamValueQuoted:
            case State.ParamValueQuotedEscape:
                Error(MimeTypeParseError.EndQuoteMissing, position);
                builder.WithParameter(pname.ToString(), pvalue.ToString());
                break;
            case State.ParamValueQuotedFinalize:
                builder.WithParameter(pname.ToString(), pvalue.ToString());
                break;
            case State.ParamValue:
            case State.ParamValueFinalize:
                if (c == '=')
                {
                    Error(MimeTypeParseError.NoParamValue, position);
                }
                else
                {
                    TrimTrailingWhitespace(pvalue, position, wsstart);
                    builder.WithParameter(pname.ToString(), pvalue.ToString());
                }

                break;
        }

        return builder.WithType(type.ToString()).WithSubtype(subtype.ToString()).Build();
    }

    // The value collected whitespace from wsstart to position (all of it single UTF-16 units): drop it.
    private static void TrimTrailingWhitespace(StringBuilder value, int position, int wsstart)
    {
        if (wsstart >= 0)
        {
            value.Remove(value.Length - position + wsstart, position - wsstart);
        }
    }

    private void Failure(MimeTypeParseError error, int position) => _errorHandler?.OnFailure(error, position);

    private void Error(MimeTypeParseError error, int position) => _errorHandler?.OnError(error, position);
}
