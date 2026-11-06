using System;

namespace Signet.EpubCheck.Core.Galimatias;

/// <summary>What kind of problem a <see cref="GalimatiasParseException"/> is (galimatias' <c>ParseIssue</c>).</summary>
public enum ParseIssue
{
    /// <summary>Not one of the others.</summary>
    Unspecified,

    /// <summary>A relative URL without a base that allows it.</summary>
    MissingScheme,

    /// <summary>A <c>%</c> not followed by two hexadecimal digits.</summary>
    InvalidPercentEncoding,

    /// <summary>A backslash used as a path delimiter.</summary>
    BackslashAsDelimiter,

    /// <summary>A tab, line feed or carriage return.</summary>
    IllegalWhitespace,

    /// <summary>A character not allowed where it is.</summary>
    IllegalCharacter,

    /// <summary>A host that cannot be parsed.</summary>
    InvalidHost,
}

/// <summary>A URL, or a part of one, that cannot be parsed (galimatias' <c>GalimatiasParseException</c>).</summary>
public sealed class GalimatiasParseException : Exception
{
    /// <summary>Creates the exception.</summary>
    public GalimatiasParseException()
    {
    }

    /// <summary>Creates the exception with its message.</summary>
    public GalimatiasParseException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its message and cause.</summary>
    public GalimatiasParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with its message, the position in the input, the issue and the cause.</summary>
    public GalimatiasParseException(string message, int position, ParseIssue issue = ParseIssue.Unspecified, Exception? innerException = null)
        : base(message, innerException)
    {
        Position = position;
        Issue = issue;
    }

    /// <summary>The position in the input (in UTF-16 units), or -1 when not known.</summary>
    public int Position { get; } = -1;

    /// <summary>The kind of problem.</summary>
    public ParseIssue Issue { get; }
}

/// <summary>
/// Receives the problems of a parse (galimatias' <c>ErrorHandler</c>): <see cref="OnError"/> for the ones the parser recovers
/// from (it may throw to stop), <see cref="OnFatalError"/> for the ones it throws for anyway.
/// </summary>
public interface IGalimatiasErrorHandler
{
    /// <summary>A problem the parser recovers from.</summary>
    void OnError(GalimatiasParseException exception);

    /// <summary>A problem the parser stops at.</summary>
    void OnFatalError(GalimatiasParseException exception);
}
