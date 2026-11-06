using System;
using System.Collections.Generic;

namespace Signet.EpubCheck.Core.Jing.Parse.Compact;

/// <summary>
/// The characters of a compact syntax schema as Jing's edited <c>JavaCharStream</c> hands them to the lexer: line ends
/// become <see cref="NewlineMarker"/>, <c>\x{…}</c> escapes are replaced by the character they stand for, characters
/// XML does not allow are an error, and each character has the line and column Jing gives it (a tab moves to the next
/// multiple of 8). Characters are decoded when the lexer first asks for them, so an error in an escape is reported only
/// once the lexer gets there, as in Jing.
/// </summary>
internal sealed class CompactCharStream
{
    /// <summary>What a line end (CR, LF or CR LF) becomes.</summary>
    public const char NewlineMarker = '\u0000';

    private const int TabSize = 8;
    private const char Bom = '﻿';
    private const char EscapeStart = '\\';
    private const char EscapeX = 'x';
    private const char EscapeOpen = '{';
    private const char EscapeClose = '}';
    private const char FirstAllowedChar = ' ';
    private const int HexRadixBits = 4;
    private const int MaxBmp = 0xFFFF;
    private const int CodePointLimit = 0x110000;

    private readonly string _raw;
    private readonly List<char> _chars = [];
    private readonly List<int> _lines = [];
    private readonly List<int> _columns = [];
    private int _rawPos;
    private int _line = 1;
    private int _column;
    private bool _prevCharIsLf;

    public CompactCharStream(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _raw = text;
        if (_raw.Length > 0 && _raw[0] == Bom)
        {
            _rawPos = 1;
        }
    }

    /// <summary>The line where the input ended (Jing puts the end of the input after the last character read).</summary>
    public int EndLine => _line;

    /// <summary>The column where the input ended.</summary>
    public int EndColumn => _column;

    /// <summary>The character at <paramref name="index"/>, or <c>false</c> at the end of the input.</summary>
    /// <exception cref="EscapeSyntaxException">The input has a character or an escape that is not allowed.</exception>
    public bool TryGet(int index, out char c)
    {
        while (_chars.Count <= index)
        {
            if (!DecodeNext())
            {
                c = default;
                return false;
            }
        }

        c = _chars[index];
        return true;
    }

    public int LineAt(int index) => _lines[index];

    public int ColumnAt(int index) => _columns[index];

    /// <summary>The characters from <paramref name="start"/> (inclusive) to <paramref name="end"/> (exclusive).</summary>
    public string Substring(int start, int end)
    {
        char[] buffer = new char[end - start];
        _chars.CopyTo(start, buffer, 0, buffer.Length);
        return new string(buffer);
    }

    private bool DecodeNext()
    {
        if (_rawPos >= _raw.Length)
        {
            return false;
        }

        char c = _raw[_rawPos++];
        switch (c)
        {
            case '\r':
                c = NewlineMarker;
                if (_rawPos < _raw.Length && _raw[_rawPos] == '\n')
                {
                    _rawPos++;
                }

                break;
            case '\n':
                c = NewlineMarker;
                break;
            case '\t':
                break;
            default:
                if (c < FirstAllowedChar || c is '￾' or '￿')
                {
                    throw new EscapeSyntaxException("illegal_char_code", _line, _column + 1);
                }

                if (char.IsSurrogate(c))
                {
                    AddSurrogatePair(c);
                    return true;
                }

                break;
        }

        UpdateLineColumn(c);
        Add(c, _line, _column);
        if (c == EscapeStart && _rawPos < _raw.Length && _raw[_rawPos] == EscapeX)
        {
            DecodeEscape();
        }

        return true;
    }

    // Jing stores the first half of a pair without moving the column: the pair counts as one character.
    private void AddSurrogatePair(char high)
    {
        if (char.IsLowSurrogate(high) || _rawPos >= _raw.Length)
        {
            throw new EscapeSyntaxException("illegal_surrogate_pair", _line, _column + 1);
        }

        char low = _raw[_rawPos++];
        if (!char.IsLowSurrogate(low))
        {
            throw new EscapeSyntaxException("illegal_surrogate_pair", _line, _column + 2);
        }

        UpdateLineColumn(low);
        Add(high, _line, _column);
        Add(low, _line, _column);
    }

    // The backslash is already stored. "\x…x{hex}" replaces it with the character (which keeps the position of the
    // backslash); without the brace the backslash and the x's stay as they are.
    private void DecodeEscape()
    {
        int backslash = _chars.Count - 1;
        List<(int Line, int Column)> xs = [];
        while (true)
        {
            _rawPos++;
            UpdateLineColumn(EscapeX);
            xs.Add((_line, _column));
            if (_rawPos >= _raw.Length || _raw[_rawPos] is not (EscapeX or EscapeOpen))
            {
                foreach ((int line, int column) in xs)
                {
                    Add(EscapeX, line, column);
                }

                return;
            }

            if (_raw[_rawPos] == EscapeOpen)
            {
                _rawPos++;
                _column++;
                break;
            }
        }

        int scalarValue = ReadHexDigit();
        while (true)
        {
            if (_rawPos >= _raw.Length)
            {
                throw new EscapeSyntaxException("incomplete_escape", _line, _column);
            }

            if (_raw[_rawPos] == EscapeClose)
            {
                _rawPos++;
                break;
            }

            scalarValue = (scalarValue << HexRadixBits) | ReadHexDigit();
            if (scalarValue >= CodePointLimit)
            {
                throw new EscapeSyntaxException("char_code_too_big", _line, _column);
            }
        }

        _column++;
        if (scalarValue <= MaxBmp)
        {
            char c = (char)scalarValue;
            if (c is not ('\n' or '\r' or '\t') && (c < FirstAllowedChar || char.IsSurrogate(c) || c is '￾' or '￿'))
            {
                throw new EscapeSyntaxException("illegal_char_code_ref", _line, _column);
            }

            _chars[backslash] = c;
            return;
        }

        string pair = char.ConvertFromUtf32(scalarValue);
        _chars[backslash] = pair[0];
        Add(pair[1], _lines[backslash], _columns[backslash]);
    }

    private int ReadHexDigit()
    {
        if (_rawPos >= _raw.Length)
        {
            throw new EscapeSyntaxException("incomplete_escape", _line, _column);
        }

        char c = _raw[_rawPos++];
        _column++;
        int value = HexValue(c);
        return value >= 0 ? value : throw new EscapeSyntaxException("illegal_hex_digit", _line, _column);
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private void UpdateLineColumn(char c)
    {
        _column++;
        if (_prevCharIsLf)
        {
            _prevCharIsLf = false;
            _line++;
            _column = 1;
        }

        if (c == NewlineMarker)
        {
            _prevCharIsLf = true;
        }
        else if (c == '\t')
        {
            _column--;
            _column += TabSize - (_column % TabSize);
        }
    }

    private void Add(char c, int line, int column)
    {
        _chars.Add(c);
        _lines.Add(line);
        _columns.Add(column);
    }
}

/// <summary>A character or an escape of the input is not allowed (Jing's <c>EscapeSyntaxException</c>).</summary>
internal sealed class EscapeSyntaxException : Exception
{
    public EscapeSyntaxException()
    {
        Key = string.Empty;
    }

    public EscapeSyntaxException(string message)
        : base(message)
    {
        Key = message;
    }

    public EscapeSyntaxException(string message, Exception innerException)
        : base(message, innerException)
    {
        Key = message;
    }

    public EscapeSyntaxException(string key, int lineNumber, int columnNumber)
        : base(key)
    {
        Key = key;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
    }

    /// <summary>The key of the message.</summary>
    public string Key { get; }

    public int LineNumber { get; }

    public int ColumnNumber { get; }
}
