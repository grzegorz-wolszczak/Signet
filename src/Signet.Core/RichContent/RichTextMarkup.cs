using System;
using System.Text;

namespace Signet.Core.RichContent;

/// <summary>
/// The light markup of a <see cref="RichText"/> written in the resources (so a translator sees text, not XAML):
/// <list type="bullet">
/// <item><c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>; a backslash takes the next character literally (<c>\*</c>,
/// <c>\`</c>, <c>\\</c>);</item>
/// <item>a line break is a line break; a blank line starts a new paragraph;</item>
/// <item>a line holding only <c>---</c> is a separator;</item>
/// <item><c>```css</c> … <c>```</c> on lines of their own is a block of code highlighted as CSS (<c>html</c>, <c>xml</c>,
/// or nothing after the backticks for none); the markup inside is not read.</item>
/// </list>
/// An unclosed mark is taken literally.
/// </summary>
public static class RichTextMarkup
{
    private const string Fence = "```";

    /// <summary>Reads <paramref name="markup"/>.</summary>
    public static RichText Parse(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        RichTextBuilder builder = new();
        string[] lines = markup.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        bool lineInParagraph = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.TrimStart().StartsWith(Fence, StringComparison.Ordinal))
            {
                string language = line.Trim()[Fence.Length..].Trim();
                StringBuilder code = new();
                int end = i + 1;
                while (end < lines.Length && lines[end].Trim() != Fence)
                {
                    if (end > i + 1)
                    {
                        code.Append('\n');
                    }

                    code.Append(lines[end]);
                    end++;
                }

                builder.CodeBlock(code.ToString(), language);
                i = end;
                lineInParagraph = false;
            }
            else if (line.Trim() == "---")
            {
                builder.Separator();
                lineInParagraph = false;
            }
            else if (line.Trim().Length == 0)
            {
                builder.Paragraph();
                lineInParagraph = false;
            }
            else
            {
                if (lineInParagraph)
                {
                    builder.LineBreak();
                }

                AppendInline(builder, line);
                lineInParagraph = true;
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// A plain text as markup that reads back as the same text (marks and backslashes escaped), for a value put into a
    /// markup template (<c>string.Format</c> of a resource).
    /// </summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        StringBuilder escaped = new(text.Length);
        foreach (char ch in text)
        {
            if (ch is '\\' or '*' or '`')
            {
                escaped.Append('\\');
            }

            escaped.Append(ch);
        }

        return escaped.ToString();
    }

    /// <summary>A plain text put between backticks in markup (only <c>`</c> and backslashes escaped).</summary>
    public static string EscapeCode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal);
    }

    // The marks of one line: **bold**, *italic*, `code`, \x.
    private static void AppendInline(RichTextBuilder builder, string line)
    {
        StringBuilder plain = new();
        int i = 0;
        while (i < line.Length)
        {
            char ch = line[i];
            if (ch == '\\' && i + 1 < line.Length)
            {
                plain.Append(line[i + 1]);
                i += 2;
                continue;
            }

            (string Open, RichSpanKind Kind)? mark = ch switch
            {
                '`' => ("`", RichSpanKind.Code),
                '*' when i + 1 < line.Length && line[i + 1] == '*' => ("**", RichSpanKind.Bold),
                '*' => ("*", RichSpanKind.Italic),
                _ => null,
            };
            int close = mark is { } m ? FindClose(line, i + m.Open.Length, m.Open) : -1;
            if (mark is not { } found || close < 0)
            {
                plain.Append(ch);
                i++;
                continue;
            }

            Flush(builder, plain);
            string inner = Unescape(line[(i + found.Open.Length)..close], keepAll: found.Kind == RichSpanKind.Code);
            switch (found.Kind)
            {
                case RichSpanKind.Bold:
                    builder.Bold(inner);
                    break;
                case RichSpanKind.Italic:
                    builder.Italic(inner);
                    break;
                default:
                    builder.Code(inner);
                    break;
            }

            i = close + found.Open.Length;
        }

        Flush(builder, plain);
    }

    private static void Flush(RichTextBuilder builder, StringBuilder plain)
    {
        if (plain.Length > 0)
        {
            builder.Text(plain.ToString());
            plain.Clear();
        }
    }

    // The first unescaped `close` from `start` with something before it, or -1.
    private static int FindClose(string line, int start, string close)
    {
        for (int i = start; i <= line.Length - close.Length; i++)
        {
            if (line[i] == '\\')
            {
                i++;
                continue;
            }

            if (i > start && string.CompareOrdinal(line, i, close, 0, close.Length) == 0
                && !(close == "*" && i + 1 < line.Length && line[i + 1] == '*'))
            {
                return i;
            }
        }

        return -1;
    }

    // The escapes of a marked fragment: \x is x (in code only \` and \\, so a backslash of code stays).
    private static string Unescape(string text, bool keepAll)
    {
        StringBuilder result = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && (!keepAll || text[i + 1] is '`' or '\\'))
            {
                result.Append(text[i + 1]);
                i++;
            }
            else
            {
                result.Append(text[i]);
            }
        }

        return result.ToString();
    }
}
