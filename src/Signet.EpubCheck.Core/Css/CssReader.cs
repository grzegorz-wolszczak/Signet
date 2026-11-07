using System;
using System.Collections.Generic;
using System.IO;

namespace Signet.EpubCheck.Core.Css;

/// <summary>
/// The character reader of the CSS scanner (upstream's <c>CssReader</c>): a reader with a pushback buffer that keeps the line, the
/// column and the offset in step with what was pushed back. A character is read as a UTF-16 unit, and -1 is the end of the input.
/// </summary>
internal sealed class CssReader
{
    /// <summary>The default size of the pushback buffer.</summary>
    public const int DefaultPushbackBufferSize = 8096;

    private readonly int[] _buffer;
    private readonly TextReader _input;
    private int _position;
    private int _prevLine = 1;

    public CssReader(TextReader reader, string systemId, int pushbackSize)
    {
        _input = reader ?? throw new ArgumentNullException(nameof(reader));
        SystemId = systemId ?? throw new ArgumentNullException(nameof(systemId));
        ArgumentOutOfRangeException.ThrowIfLessThan(pushbackSize, 1);

        _position = pushbackSize;
        _buffer = new int[pushbackSize];
    }

    /// <summary>The system id of the source.</summary>
    public string SystemId { get; }

    /// <summary>The current character.</summary>
    public int CurChar { get; private set; }

    /// <summary>The character before the current one.</summary>
    public int PrevChar { get; private set; }

    /// <summary>The offset of the current character.</summary>
    public int Offset { get; private set; }

    /// <summary>The line of the current character, from 1.</summary>
    public int Line { get; private set; } = 1;

    /// <summary>The column of the current character, from 1.</summary>
    public int Column { get; private set; } = 1;

    /// <summary>Reads the next character (-1 at the end).</summary>
    public int Next()
    {
        PrevChar = CurChar;
        if (PrevChar <= -1)
        {
            throw new InvalidOperationException("Read after the end of the input.");
        }

        CurChar = _position < _buffer.Length ? _buffer[_position++] : _input.Read();
        Offset++;
        if (CurChar is '\r' or '\n')
        {
            // a '\n' after a '\r' is the same line end
            if (!(CurChar == '\n' && PrevChar == '\r'))
            {
                _prevLine = Line;
                Line++;
            }
        }
        else if (_prevLine < Line)
        {
            Column = 1;
            _prevLine = Line;
        }
        else if (PrevChar != 0)
        {
            Column++;
        }

        return CurChar;
    }

    /// <summary>The next character, without reading it.</summary>
    public int Peek()
    {
        Mark mark = GetMark();
        int ch = Next();
        Unread(ch, mark);
        return ch;
    }

    /// <summary>The next <paramref name="n"/> characters, without reading them (-1 after the end).</summary>
    public int[] Peek(int n)
    {
        int[] buffer = new int[n];
        Mark mark = GetMark();
        bool seenEof = false;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = seenEof ? -1 : Next();
            if (buffer[i] == -1)
            {
                seenEof = true;
            }
        }

        if (!seenEof)
        {
            Unread(buffer, mark);
        }
        else
        {
            List<int> ints = [];
            foreach (int ch in buffer)
            {
                ints.Add(ch);
                if (ch == -1)
                {
                    break;
                }
            }

            Unread(ints, mark);
        }

        return buffer;
    }

    /// <summary>The character <paramref name="n"/> places ahead, without reading it.</summary>
    public int At(int n)
    {
        Mark mark = GetMark();
        List<int> read = [];
        for (int i = 0; i < n; i++)
        {
            read.Add(Next());
            if (CurChar == -1)
            {
                break;
            }
        }

        Unread(read, mark);
        return read[^1];
    }

    /// <summary>Reads the next <paramref name="n"/> characters.</summary>
    public int[] Collect(int n)
    {
        int[] buffer = new int[n];
        bool seenEof = false;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (seenEof)
            {
                buffer[i] = -1;
            }
            else
            {
                buffer[i] = Next();
                if (CurChar == -1)
                {
                    seenEof = true;
                }
            }
        }

        return buffer;
    }

    /// <summary>Reads on until the next character is one <paramref name="matcher"/> matches (not escaped) or the end.</summary>
    public CssReader Forward(CharMatcher matcher)
    {
        while (true)
        {
            Mark mark = GetMark();
            Next();
            if (CurChar == -1 || (matcher.Matches((char)CurChar) && PrevChar != '\\'))
            {
                Unread(CurChar, mark);
                break;
            }
        }

        return this;
    }

    /// <summary>Reads <paramref name="n"/> characters, fewer at the end.</summary>
    public CssReader Forward(int n)
    {
        for (int i = 0; i < n; i++)
        {
            Mark mark = GetMark();
            Next();
            if (CurChar == -1)
            {
                Unread(CurChar, mark);
                break;
            }
        }

        return this;
    }

    public void Unread(int ch, Mark mark)
    {
        if (_position <= 0)
        {
            throw new InvalidOperationException("The pushback buffer is full.");
        }

        _buffer[--_position] = ch;
        Reset(mark);
    }

    public void Unread(int[] chars, Mark mark) => Unread(chars, 0, chars.Length, mark);

    public void Unread(List<int> chars, Mark mark) => Unread(chars.ToArray(), mark);

    public void Unread(int[] chars, int offset, int length, Mark mark)
    {
        if (length >= _position)
        {
            throw new ArgumentException("The pushback buffer is full.");
        }

        _position -= length;
        Array.Copy(chars, offset, _buffer, _position, length);
        Reset(mark);
    }

    public Mark GetMark() => new(CurChar, PrevChar, Line, Column, _prevLine, Offset);

    private void Reset(Mark mark)
    {
        CurChar = mark.Cur;
        PrevChar = mark.Prev;
        Line = mark.Line;
        Column = mark.Column;
        _prevLine = mark.PrevLine;
        Offset = mark.Offset;
    }

    public override string ToString() => $"CssReader{{curChar={(char)CurChar}, prevChar={(char)PrevChar}}}";

    /// <summary>A position of the reader to go back to.</summary>
    internal readonly record struct Mark(int Cur, int Prev, int Line, int Column, int PrevLine, int Offset);
}
