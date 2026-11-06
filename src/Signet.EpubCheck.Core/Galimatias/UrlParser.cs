using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Galimatias;

/// <summary>The states of the parser (the URL standard of 2014 that galimatias implements).</summary>
internal enum ParseUrlState
{
    SchemeStart,
    Scheme,
    SchemeData,
    NoScheme,
    RelativeOrAuthority,
    Relative,
    RelativeSlash,
    AuthorityFirstSlash,
    AuthoritySecondSlash,
    AuthorityIgnoreSlashes,
    Authority,
    FileHost,
    Host,
    Port,
    RelativePathStart,
    RelativePath,
    Query,
    Fragment,
}

/// <summary>
/// The URL parser of galimatias (<c>URLParser</c>), ported state by state. The index handling is kept as it is in Java:
/// some states step back with <c>idx--</c> without reading the code point again (the code point of the last read stays
/// in <c>c</c>), others with <see cref="DecrIdx"/>; the loop then moves on by the length of <c>c</c>.
/// </summary>
internal sealed class UrlParser
{
    private const int MaxPort = ushort.MaxValue;
    private const int Tab = 0x0009;
    private const int LineFeed = 0x000A;
    private const int CarriageReturn = 0x000D;
    private const int EscapeLength = 3; // "%XY"
    private const int FirstPrintable = 0x0020;
    private const int LastPrintable = 0x007E;
    private const int FirstQueryPrintable = 0x21;
    private const string EncodedAtSign = "%40";

    private readonly Url? _base;
    private readonly string _input;
    private readonly Url? _url;
    private readonly ParseUrlState? _stateOverride;
    private readonly IGalimatiasErrorHandler? _errorHandler;
    private int _startIdx;
    private int _endIdx;
    private int _idx;
    private bool _isEof;
    private int _c;

    public UrlParser(Url? baseUrl, string input, Url? url = null, ParseUrlState? stateOverride = null, IGalimatiasErrorHandler? errorHandler = null)
    {
        _base = baseUrl;
        _input = input ?? throw new ArgumentNullException(nameof(input), "null input");
        _url = url;
        _stateOverride = stateOverride;
        _errorHandler = errorHandler;
    }

    private enum EncodeSet
    {
        Simple,
        Default,
        Password,
        Username,
    }

    /// <summary>": space is not allowed", or ": “x” is not allowed" — the end of galimatias' illegal character messages.</summary>
    internal static string DescribeIllegal(int codePoint) => codePoint switch
    {
        ' ' => ": space is not allowed",
        Tab => ": tab is not allowed",
        LineFeed => ": line break is not allowed",
        CarriageReturn => ": carriage return is not allowed",
        _ => ": “" + CodePointToString(codePoint) + "” is not allowed",
    };

    public Url Parse()
    {
        StringBuilder buffer = new(_input.Length * 2);
        string? scheme = _url?.Scheme;
        StringBuilder schemeData = new(_url?.SchemeData ?? string.Empty);
        string? username = _url?.Username;
        string? password = _url?.Password;
        Host? host = _url?.Host;
        int port = _url is null ? -1 : _url.Port;
        bool relativeFlag = _url is not null && _url.IsHierarchical;
        bool atFlag = false;
        bool bracketsFlag = false;
        List<string>? pathSegments = _url is null || _stateOverride == ParseUrlState.RelativePathStart ? [] : _url.PathSegments;
        StringBuilder? query = _url?.Query is null || _stateOverride == ParseUrlState.Query ? null : new StringBuilder(_url.Query);
        StringBuilder? fragment = _url?.Fragment is null || _stateOverride == ParseUrlState.Fragment ? null : new StringBuilder(_url.Fragment);
        StringBuilder usernameBuffer = new();
        StringBuilder? passwordBuffer = null;

        _endIdx = _input.Length;
        SetIdx(_startIdx);

        // Leading and trailing white space (Java's Character.isWhitespace).
        while (UrlUtils.IsJavaWhitespace(_c))
        {
            IncIdx();
            _startIdx++;
        }

        while (_endIdx > _startIdx && UrlUtils.IsJavaWhitespace(_input[_endIdx - 1]))
        {
            _endIdx--;
        }

        ParseUrlState state = _stateOverride ?? ParseUrlState.SchemeStart;
        bool terminate = false;
        while (!terminate)
        {
            if (_idx > _endIdx)
            {
                break;
            }

            switch (state)
            {
                case ParseUrlState.SchemeStart:
                    if (UrlUtils.IsAsciiAlpha(_c))
                    {
                        buffer.Append(char.ToLowerInvariant((char)_c));
                        state = ParseUrlState.Scheme;
                    }
                    else if (_stateOverride is null)
                    {
                        state = ParseUrlState.NoScheme;
                        DecrIdx();
                    }
                    else
                    {
                        HandleFatalError("Scheme must start with alpha character.");
                    }

                    break;
                case ParseUrlState.Scheme:
                    if (UrlUtils.IsAsciiAlphanumeric(_c) || _c is '+' or '-' or '.')
                    {
                        buffer.Append(char.ToLowerInvariant((char)_c));
                    }
                    else if (_c == ':')
                    {
                        scheme = buffer.ToString();
                        buffer.Clear();
                        if (_stateOverride is not null)
                        {
                            terminate = true;
                            break;
                        }

                        relativeFlag = UrlUtils.IsRelativeScheme(scheme);
                        if (scheme == Url.FileScheme)
                        {
                            state = ParseUrlState.Relative;
                        }
                        else if (relativeFlag && _base is not null && _base.Scheme == scheme)
                        {
                            state = ParseUrlState.RelativeOrAuthority;
                        }
                        else if (relativeFlag)
                        {
                            state = ParseUrlState.AuthorityFirstSlash;
                        }
                        else
                        {
                            state = ParseUrlState.SchemeData;
                        }
                    }
                    else if (_stateOverride is null)
                    {
                        // Not a scheme: start over without one (the index is set to the start after the switch).
                        buffer.Clear();
                        state = ParseUrlState.NoScheme;
                        _idx = -1;
                    }
                    else if (_isEof)
                    {
                        terminate = true;
                    }
                    else
                    {
                        HandleFatalIllegalCharacterError("Illegal character in scheme", _c);
                    }

                    break;
                case ParseUrlState.SchemeData:
                    if (_c == '?')
                    {
                        query = new StringBuilder();
                        state = ParseUrlState.Query;
                    }
                    else if (_c == '#')
                    {
                        fragment = new StringBuilder();
                        state = ParseUrlState.Fragment;
                    }
                    else
                    {
                        if (!_isEof && _c != '%' && !UrlUtils.IsUrlCodePoint(_c))
                        {
                            HandleIllegalCharacterError("Illegal character in scheme data", _c);
                        }

                        if (_c == '%')
                        {
                            if (TryAppendEscape(schemeData))
                            {
                                break;
                            }
                        }

                        if (!_isEof && _c is not (Tab or LineFeed or CarriageReturn))
                        {
                            Utf8PercentEncode(_c, EncodeSet.Simple, schemeData);
                        }
                    }

                    break;
                case ParseUrlState.NoScheme:
                    if (_base is null || !UrlUtils.IsRelativeScheme(_base.Scheme))
                    {
                        HandleFatalError("Missing scheme", ParseIssue.MissingScheme);
                    }

                    state = ParseUrlState.Relative;
                    _idx--;
                    break;
                case ParseUrlState.RelativeOrAuthority:
                    if (_c == '/' && At(_idx + 1) == '/')
                    {
                        state = ParseUrlState.AuthorityIgnoreSlashes;
                        _idx++;
                    }
                    else
                    {
                        HandleError("Relative scheme (" + scheme + ") is not followed by \"://\"");
                        state = ParseUrlState.Relative;
                        _idx--;
                    }

                    break;
                case ParseUrlState.Relative:
                    relativeFlag = true;
                    if (scheme != Url.FileScheme)
                    {
                        scheme = _base?.Scheme;
                    }

                    if (_isEof)
                    {
                        (host, port) = BaseHostAndPort();
                        pathSegments = _base?.PathSegments;
                        query = _base?.Query is null ? null : new StringBuilder(_base.Query);
                    }
                    else if (_c is '/' or '\\')
                    {
                        if (_c == '\\')
                        {
                            HandleBackslashAsDelimiterError();
                        }

                        state = ParseUrlState.RelativeSlash;
                    }
                    else if (_c == '?')
                    {
                        (host, port) = BaseHostAndPort();
                        pathSegments = _base?.PathSegments;
                        query = new StringBuilder();
                        state = ParseUrlState.Query;
                    }
                    else if (_c == '#')
                    {
                        (host, port) = BaseHostAndPort();
                        pathSegments = _base?.PathSegments;
                        query = _base?.Query is null ? null : new StringBuilder(_base.Query);
                        fragment = new StringBuilder();
                        state = ParseUrlState.Fragment;
                    }
                    else
                    {
                        // Unless this is the drive letter of a file URL ("C:" or "C|" then a delimiter or the end),
                        // start from the base's path without its last segment.
                        if (scheme != Url.FileScheme
                            || !UrlUtils.IsAsciiAlpha(_c)
                            || (At(_idx + 1) != ':' && At(_idx + 1) != '|')
                            || _idx + 1 == _endIdx - 1
                            || (_idx + 2 < _endIdx && At(_idx + 2) is not ('/' or '\\' or '?' or '#')))
                        {
                            (host, port) = BaseHostAndPort();
                            pathSegments = _base is null ? [] : _base.PathSegments;
                            if (pathSegments!.Count != 0)
                            {
                                pathSegments.RemoveAt(pathSegments.Count - 1);
                            }
                        }

                        state = ParseUrlState.RelativePath;
                        _idx--;
                    }

                    break;
                case ParseUrlState.RelativeSlash:
                    if (_c is '/' or '\\')
                    {
                        if (_c == '\\')
                        {
                            HandleBackslashAsDelimiterError();
                        }

                        state = scheme == Url.FileScheme ? ParseUrlState.FileHost : ParseUrlState.AuthorityIgnoreSlashes;
                    }
                    else
                    {
                        if (scheme != Url.FileScheme)
                        {
                            (host, port) = BaseHostAndPort();
                        }

                        state = ParseUrlState.RelativePath;
                        _idx--;
                    }

                    break;
                case ParseUrlState.AuthorityFirstSlash:
                    if (_c == '/')
                    {
                        state = ParseUrlState.AuthoritySecondSlash;
                    }
                    else
                    {
                        HandleError("Expected a slash (\"/\")");
                        state = ParseUrlState.AuthorityIgnoreSlashes;
                        DecrIdx();
                    }

                    break;
                case ParseUrlState.AuthoritySecondSlash:
                    if (_c != '/')
                    {
                        HandleError("Expected a slash (\"/\")");
                        DecrIdx();
                    }

                    state = ParseUrlState.AuthorityIgnoreSlashes;
                    break;
                case ParseUrlState.AuthorityIgnoreSlashes:
                    if (_c is not ('/' or '\\'))
                    {
                        state = ParseUrlState.Authority;
                        DecrIdx();
                    }
                    else
                    {
                        HandleError("Unexpected slash or backslash");
                    }

                    break;
                case ParseUrlState.Authority:
                    if (_c == '@')
                    {
                        if (atFlag)
                        {
                            HandleError("User or password contains an at symbol (\"@\") not percent-encoded");
                            buffer.Insert(0, EncodedAtSign);
                        }

                        atFlag = true;
                        passwordBuffer = ParseUserInfo(buffer, usernameBuffer, passwordBuffer);
                        buffer.Clear();
                    }
                    else if (_isEof || _c is '/' or '\\' or '?' or '#')
                    {
                        SetIdx(_idx - buffer.Length - 1);
                        if (atFlag)
                        {
                            username = usernameBuffer.ToString();
                            if (passwordBuffer is not null)
                            {
                                password = passwordBuffer.ToString();
                            }
                        }

                        buffer.Clear();
                        state = ParseUrlState.Host;
                    }
                    else
                    {
                        buffer.Append(CodePointToString(_c));
                    }

                    break;
                case ParseUrlState.FileHost:
                    if (_isEof || _c is '/' or '\\' or '?' or '#')
                    {
                        _idx--;
                        if (buffer.Length == 2 && UrlUtils.IsAsciiAlpha(buffer[0]) && buffer[1] is ':' or '|')
                        {
                            state = ParseUrlState.RelativePath;
                        }
                        else if (buffer.Length == 0)
                        {
                            state = ParseUrlState.RelativePathStart;
                        }
                        else
                        {
                            host = ParseHost(buffer.ToString());
                            buffer.Clear();
                            state = ParseUrlState.RelativePathStart;
                        }
                    }
                    else if (_c is Tab or LineFeed or CarriageReturn)
                    {
                        HandleIllegalWhitespaceError();
                    }
                    else
                    {
                        buffer.Append(CodePointToString(_c));
                    }

                    break;
                case ParseUrlState.Host:
                    if (_c == ':' && !bracketsFlag)
                    {
                        host = ParseHost(buffer.ToString());
                        buffer.Clear();
                        state = ParseUrlState.Port;
                        if (_stateOverride == ParseUrlState.Host)
                        {
                            terminate = true;
                        }
                    }
                    else if (_isEof || _c is '/' or '\\' or '?' or '#')
                    {
                        DecrIdx();
                        host = ParseHost(buffer.ToString());
                        buffer.Clear();
                        state = ParseUrlState.RelativePathStart;
                        if (_stateOverride is not null)
                        {
                            terminate = true;
                        }
                    }
                    else if (_c is Tab or LineFeed or CarriageReturn)
                    {
                        HandleIllegalWhitespaceError();
                    }
                    else
                    {
                        if (_c == '[')
                        {
                            bracketsFlag = true;
                        }
                        else if (_c == ']')
                        {
                            bracketsFlag = false;
                        }

                        buffer.Append(CodePointToString(_c));
                    }

                    break;
                case ParseUrlState.Port:
                    if (UrlUtils.IsAsciiDigit(_c))
                    {
                        buffer.Append((char)_c);
                    }
                    else if (_isEof || _c is '/' or '\\' or '?' or '#')
                    {
                        while (buffer.Length > 1 && buffer[0] == '0')
                        {
                            buffer.Remove(0, 1);
                        }

                        if (buffer.ToString() == UrlUtils.GetDefaultPortForScheme(scheme)?.ToString(CultureInfo.InvariantCulture))
                        {
                            buffer.Clear();
                        }

                        if (buffer.Length == 0)
                        {
                            port = -1;
                        }
                        else if (int.TryParse(buffer.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
                        {
                            port = parsed;
                            if (port > MaxPort)
                            {
                                HandleError("Port number must be less than 65536");
                            }
                        }
                        else
                        {
                            HandleError("Port number must be less than 65536");
                        }

                        if (_stateOverride is not null)
                        {
                            terminate = true;
                        }
                        else
                        {
                            buffer.Clear();
                            state = ParseUrlState.RelativePathStart;
                            _idx--;
                        }
                    }
                    else if (_c is Tab or LineFeed or CarriageReturn)
                    {
                        HandleIllegalWhitespaceError();
                    }
                    else
                    {
                        HandleFatalIllegalCharacterError("Illegal character in port", _c);
                    }

                    break;
                case ParseUrlState.RelativePathStart:
                    if (_c == '\\')
                    {
                        HandleBackslashAsDelimiterError();
                    }

                    state = ParseUrlState.RelativePath;
                    if (_c is not ('/' or '\\'))
                    {
                        DecrIdx();
                    }

                    break;
                case ParseUrlState.RelativePath:
                    if (_isEof || _c is '/' or '\\' || (_stateOverride is null && _c is '?' or '#'))
                    {
                        if (_c == '\\')
                        {
                            HandleBackslashAsDelimiterError();
                        }

                        pathSegments ??= [];
                        EndPathSegment(buffer, pathSegments, scheme, _c);
                        buffer.Clear();
                        if (_c == '?')
                        {
                            query = new StringBuilder();
                            state = ParseUrlState.Query;
                        }
                        else if (_c == '#')
                        {
                            fragment = new StringBuilder();
                            state = ParseUrlState.Fragment;
                        }
                    }
                    else if (_c is Tab or LineFeed or CarriageReturn)
                    {
                        HandleIllegalWhitespaceError();
                    }
                    else
                    {
                        if (!UrlUtils.IsUrlCodePoint(_c) && _c != '%')
                        {
                            HandleIllegalCharacterError("Illegal character in path segment", _c);
                        }

                        if (_c == '%' && TryAppendEscape(buffer))
                        {
                            break;
                        }

                        Utf8PercentEncode(_c, EncodeSet.Default, buffer);
                    }

                    break;
                case ParseUrlState.Query:
                    query ??= new StringBuilder();
                    if (_isEof || (_stateOverride is null && _c == '#'))
                    {
                        foreach (byte b in Encoding.UTF8.GetBytes(buffer.ToString()))
                        {
                            if (b is < FirstQueryPrintable or > LastPrintable or (byte)'"' or (byte)'#' or (byte)'<' or (byte)'>' or (byte)'`')
                            {
                                UrlUtils.PercentEncode(b, query);
                            }
                            else
                            {
                                query.Append((char)b);
                            }
                        }

                        buffer.Clear();
                        if (_c == '#')
                        {
                            fragment = new StringBuilder();
                            state = ParseUrlState.Fragment;
                        }
                    }
                    else if (_c is Tab or LineFeed or CarriageReturn)
                    {
                        HandleIllegalWhitespaceError();
                    }
                    else
                    {
                        if (!UrlUtils.IsUrlCodePoint(_c) && _c != '%')
                        {
                            HandleIllegalCharacterError("Illegal character in query", _c);
                        }

                        if (_c == '%' && TryAppendEscape(buffer))
                        {
                            break;
                        }

                        buffer.Append(CodePointToString(_c));
                    }

                    break;
                case ParseUrlState.Fragment:
                    fragment ??= new StringBuilder();
                    if (_isEof)
                    {
                        // nothing
                    }
                    else if (_c is Tab or LineFeed or CarriageReturn)
                    {
                        HandleIllegalWhitespaceError();
                    }
                    else
                    {
                        if (!UrlUtils.IsUrlCodePoint(_c) && _c != '%')
                        {
                            HandleIllegalCharacterError("Illegal character in fragment", _c);
                        }

                        if (_c == '%' && TryAppendEscape(fragment))
                        {
                            break;
                        }

                        Utf8PercentEncode(_c, EncodeSet.Simple, fragment);
                    }

                    break;
            }

            if (_idx == -1)
            {
                SetIdx(_startIdx);
            }
            else
            {
                IncIdx();
            }
        }

        return new Url(scheme ?? throw new GalimatiasParseException("scheme cannot be null", -1), schemeData.ToString(),
            username, password, host, port, pathSegments,
            query?.ToString(), fragment?.ToString(), relativeFlag);
    }

    /// <summary>A user name for <see cref="Url.WithUsername"/>: percent-encoded with the user name encode set.</summary>
    public string ParseUsername() => EncodeAll(EncodeSet.Username);

    /// <summary>A password for <see cref="Url.WithPassword"/>: percent-encoded with the password encode set.</summary>
    public string ParsePassword() => EncodeAll(EncodeSet.Password);

    private static string CodePointToString(int codePoint) =>
        codePoint is >= char.MinValue and <= char.MaxValue ? ((char)codePoint).ToString() : char.ConvertFromUtf32(codePoint);

    private static bool IsInSimpleEncodeSet(int c) => c is < FirstPrintable or > LastPrintable;

    private static bool IsInDefaultEncodeSet(int c) => IsInSimpleEncodeSet(c) || c is ' ' or '"' or '#' or '<' or '>' or '?' or '`';

    private static bool IsInPasswordEncodeSet(int c) => IsInDefaultEncodeSet(c) || c is '/' or '@' or '\\';

    private static bool IsInUsernameEncodeSet(int c) => IsInPasswordEncodeSet(c) || c == ':';

    // ".", "..", and their escaped forms; a "C|" drive letter of a file URL becomes "C:". The segment ends at c: a
    // '/' or '\' (then "." and ".." add nothing), or anything else (then they leave an empty last segment).
    private static void EndPathSegment(StringBuilder buffer, List<string> pathSegments, string? scheme, int c)
    {
        string lowerCased = buffer.ToString().ToLowerInvariant();
        if (lowerCased == "%2e")
        {
            buffer.Clear().Append('.');
        }
        else if (lowerCased is ".%2e" or "%2e." or "%2e%2e")
        {
            buffer.Clear().Append("..");
        }

        string segment = buffer.ToString();
        bool atDelimiter = c is '/' or '\\';
        if (segment == "..")
        {
            if (pathSegments.Count != 0)
            {
                pathSegments.RemoveAt(pathSegments.Count - 1);
            }

            if (!atDelimiter)
            {
                pathSegments.Add(string.Empty);
            }
        }
        else if (segment == ".")
        {
            if (!atDelimiter)
            {
                pathSegments.Add(string.Empty);
            }
        }
        else
        {
            if (scheme == Url.FileScheme && pathSegments.Count == 0 && segment.Length == 2 && UrlUtils.IsAsciiAlpha(segment[0]) && segment[1] == '|')
            {
                segment = segment[0] + ":";
            }

            pathSegments.Add(segment);
        }
    }

    private (Host? Host, int Port) BaseHostAndPort() =>
        (_base?.Host, _base is null || _base.Port == _base.DefaultPort ? -1 : _base.Port);

    private StringBuilder? ParseUserInfo(StringBuilder buffer, StringBuilder usernameBuffer, StringBuilder? passwordBuffer)
    {
        string text = buffer.ToString();
        int i = 0;
        while (i < text.Length)
        {
            int codePoint = UrlUtils.CodePointAt(text, i);
            int length = codePoint > char.MaxValue ? 2 : 1;
            if (codePoint is Tab or LineFeed or CarriageReturn)
            {
                HandleIllegalWhitespaceError();
                i += length;
                continue;
            }

            if (!UrlUtils.IsUrlCodePoint(codePoint) && codePoint != '%')
            {
                HandleIllegalCharacterError("Illegal character in user or password", codePoint);
            }

            if (codePoint == '%')
            {
                if (i + 2 >= text.Length || !UrlUtils.IsAsciiHexDigit(text[i + 1]) || !UrlUtils.IsAsciiHexDigit(text[i + 2]))
                {
                    HandleInvalidPercentEncodingError();
                }
                else
                {
                    text = string.Concat(text.AsSpan(0, i + 1), char.ToUpperInvariant(text[i + 1]).ToString(),
                        char.ToUpperInvariant(text[i + 2]).ToString(), text.AsSpan(i + EscapeLength));
                }
            }

            if (codePoint == ':' && passwordBuffer is null)
            {
                passwordBuffer = new StringBuilder();
                i += length;
                continue;
            }

            Utf8PercentEncode(codePoint, EncodeSet.Default, passwordBuffer ?? usernameBuffer);
            i += length;
        }

        return passwordBuffer;
    }

    private Host ParseHost(string text)
    {
        try
        {
            return Galimatias.Host.ParseHost(text);
        }
        catch (GalimatiasParseException ex)
        {
            HandleFatalError(new GalimatiasParseException("Invalid host: " + ex.Message, _idx, ParseIssue.InvalidHost, ex));
            throw;
        }
    }

    // A "%XY" escape at the index: appended with upper-case digits, the index moved onto its last digit. Otherwise the
    // escape is reported as invalid and false is returned (the '%' is then encoded like any character).
    private bool TryAppendEscape(StringBuilder target)
    {
        if (!UrlUtils.IsAsciiHexDigit(At(_idx + 1)) || !UrlUtils.IsAsciiHexDigit(At(_idx + 2)))
        {
            HandleInvalidPercentEncodingError();
            return false;
        }

        target.Append('%').Append(char.ToUpperInvariant(_input[_idx + 1])).Append(char.ToUpperInvariant(_input[_idx + 2]));
        SetIdx(_idx + 2);
        return true;
    }

    private string EncodeAll(EncodeSet encodeSet)
    {
        StringBuilder buffer = new(_input.Length * 2);
        _startIdx = 0;
        _endIdx = _input.Length;
        SetIdx(0);
        while (!_isEof)
        {
            Utf8PercentEncode(_c, encodeSet, buffer);
            IncIdx();
        }

        return buffer.ToString();
    }

    private static void Utf8PercentEncode(int c, EncodeSet encodeSet, StringBuilder buffer)
    {
        bool encode = encodeSet switch
        {
            EncodeSet.Simple => IsInSimpleEncodeSet(c),
            EncodeSet.Default => IsInDefaultEncodeSet(c),
            EncodeSet.Password => IsInPasswordEncodeSet(c),
            _ => IsInUsernameEncodeSet(c),
        };
        if (!encode)
        {
            buffer.Append(CodePointToString(c));
            return;
        }

        foreach (byte b in Encoding.UTF8.GetBytes(CodePointToString(c)))
        {
            UrlUtils.PercentEncode(b, buffer);
        }
    }

    private void SetIdx(int i)
    {
        _idx = i;
        _isEof = i >= _endIdx;
        _c = _isEof || _idx < _startIdx ? 0 : UrlUtils.CodePointAt(_input, i);
    }

    private void IncIdx() => SetIdx(_idx + (_c > char.MaxValue ? 2 : 1));

    private void DecrIdx()
    {
        if (_idx <= _startIdx)
        {
            SetIdx(_idx - 1);
            return;
        }

        bool pairBefore = _idx >= 2 && char.IsLowSurrogate(_input[_idx - 1]) && char.IsHighSurrogate(_input[_idx - 2]);
        SetIdx(_idx - (pairBefore ? 2 : 1));
    }

    private char At(int i) => i >= _endIdx || i < 0 ? '\0' : _input[i];

    private void HandleError(string message, ParseIssue issue = ParseIssue.Unspecified) =>
        _errorHandler?.OnError(new GalimatiasParseException(message, _idx, issue));

    private void HandleFatalError(string message, ParseIssue issue = ParseIssue.Unspecified) =>
        HandleFatalError(new GalimatiasParseException(message, _idx, issue));

    private void HandleFatalError(GalimatiasParseException exception)
    {
        _errorHandler?.OnFatalError(exception);
        throw exception;
    }

    private void HandleInvalidPercentEncodingError() =>
        HandleError("Percentage (\"%\") is not followed by two hexadecimal digits", ParseIssue.InvalidPercentEncoding);

    private void HandleBackslashAsDelimiterError() =>
        HandleError("Backslash (\"\\\") used as path segment delimiter", ParseIssue.BackslashAsDelimiter);

    private void HandleIllegalWhitespaceError() =>
        HandleError("Tab, new line or carriage return found", ParseIssue.IllegalWhitespace);

    private void HandleIllegalCharacterError(string message, int codePoint) =>
        HandleError(message + DescribeIllegal(codePoint), ParseIssue.IllegalCharacter);

    private void HandleFatalIllegalCharacterError(string message, int codePoint) =>
        HandleFatalError(message + ": “" + CodePointToString(codePoint) + "” is not allowed", ParseIssue.IllegalCharacter);
}
