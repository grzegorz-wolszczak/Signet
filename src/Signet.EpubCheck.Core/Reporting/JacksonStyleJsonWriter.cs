using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Signet.EpubCheck.Core.Reporting;

/// <summary>
/// Writes JSON the way upstream's Jackson mapper does with indentation on and non-ASCII characters escaped: <c>"name" : value</c>,
/// one entry of an object per line, the values of an array on one line (<c>[ 1, 2 ]</c>, <c>[ ]</c> when empty), <c>{ }</c> for an
/// empty object and <c>\uXXXX</c> (upper-case hex) for everything beyond ASCII.
/// </summary>
internal sealed class JacksonStyleJsonWriter
{
    private const string Indentation = "  ";
    private const string HexDigits = "0123456789ABCDEF";
    private const char FirstNonControl = ' ';
    private const char LastAscii = '\u007F';

    private readonly TextWriter _writer;
    private readonly Stack<Level> _levels = new();
    private int _objectNesting;

    public JacksonStyleJsonWriter(TextWriter writer)
    {
        _writer = writer;
    }

    private sealed class Level(bool isObject)
    {
        public bool IsObject { get; } = isObject;

        public int Count { get; set; }
    }

    public void BeginObject()
    {
        BeforeValue();
        _writer.Write('{');
        _levels.Push(new Level(isObject: true));
        _objectNesting++;
    }

    public void EndObject()
    {
        Level level = _levels.Pop();
        _objectNesting--;
        if (level.Count > 0)
        {
            WriteIndentation(_objectNesting);
        }
        else
        {
            _writer.Write(' ');
        }

        _writer.Write('}');
    }

    public void BeginArray()
    {
        BeforeValue();
        _writer.Write('[');
        _levels.Push(new Level(isObject: false));
    }

    public void EndArray()
    {
        _levels.Pop();
        _writer.Write(" ]");
    }

    /// <summary>Starts an entry of the current object; the value follows.</summary>
    public void Name(string name)
    {
        Level level = _levels.Peek();
        if (!level.IsObject)
        {
            throw new InvalidOperationException("A name is only written in an object.");
        }

        if (level.Count > 0)
        {
            _writer.Write(',');
        }

        WriteIndentation(_objectNesting);
        level.Count++;
        WriteQuoted(name);
        _writer.Write(" : ");
    }

    public void String(string? value)
    {
        if (value is null)
        {
            Null();
            return;
        }

        BeforeValue();
        WriteQuoted(value);
    }

    public void Number(long value)
    {
        BeforeValue();
        _writer.Write(value.ToString(CultureInfo.InvariantCulture));
    }

    public void Boolean(bool value)
    {
        BeforeValue();
        _writer.Write(value ? "true" : "false");
    }

    public void Null()
    {
        BeforeValue();
        _writer.Write("null");
    }

    public void StringArray(IEnumerable<string> values)
    {
        BeginArray();
        foreach (string value in values)
        {
            String(value);
        }

        EndArray();
    }

    // A value in an array is preceded by a space (the first) or ", " (the others); a value of an object entry follows its name.
    private void BeforeValue()
    {
        if (_levels.Count == 0)
        {
            return;
        }

        Level level = _levels.Peek();
        if (level.IsObject)
        {
            return;
        }

        if (level.Count > 0)
        {
            _writer.Write(',');
        }

        _writer.Write(' ');
        level.Count++;
    }

    private void WriteIndentation(int nesting)
    {
        _writer.Write('\n');
        for (int i = 0; i < nesting; i++)
        {
            _writer.Write(Indentation);
        }
    }

    private void WriteQuoted(string text)
    {
        _writer.Write('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': _writer.Write("\\\""); break;
                case '\\': _writer.Write("\\\\"); break;
                case '\n': _writer.Write("\\n"); break;
                case '\r': _writer.Write("\\r"); break;
                case '\t': _writer.Write("\\t"); break;
                case '\b': _writer.Write("\\b"); break;
                case '\f': _writer.Write("\\f"); break;
                default:
                    if (c < FirstNonControl || c > LastAscii)
                    {
                        _writer.Write("\\u");
                        _writer.Write(HexDigits[(c >> 12) & 0xF]);
                        _writer.Write(HexDigits[(c >> 8) & 0xF]);
                        _writer.Write(HexDigits[(c >> 4) & 0xF]);
                        _writer.Write(HexDigits[c & 0xF]);
                    }
                    else
                    {
                        _writer.Write(c);
                    }

                    break;
            }
        }

        _writer.Write('"');
    }
}
