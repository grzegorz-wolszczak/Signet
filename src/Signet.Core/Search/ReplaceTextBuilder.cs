using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Signet.Core.Search;

/// <summary>
/// Builds the replacement text from a replacement pattern.
/// </summary>
/// <remarks>
/// Supported control sequences (parsed character by character in a single pass):
/// <list type="bullet">
///   <item><c>\0</c>..<c>\9</c> — numbered backreference,</item>
///   <item><c>\g{n}</c> / <c>\g&lt;n&gt;</c> / <c>\g{name}</c> / <c>\g&lt;name&gt;</c> — backreference,</item>
///   <item><c>\l</c> <c>\u</c> — lowercase/uppercase the next character,</item>
///   <item><c>\L</c> <c>\U</c> ... <c>\E</c> — lowercase/uppercase until <c>\E</c>,</item>
///   <item><c>\a \b \f \n \r \t \v</c> — control characters, <c>\\</c> — a literal backslash,</item>
///   <item><c>\xHH</c> and <c>\x{H..}</c> (2, 4 or 6 hex digits, up to <c>\x{10FFFF}</c>).</item>
/// </list>
/// Invalid or unsupported sequences are copied to the result literally.
/// </remarks>
public sealed class ReplaceTextBuilder
{
    private enum CaseChange
    {
        LowerNext,
        Lower,
        UpperNext,
        Upper,
        None,
    }

    private readonly StringBuilder _finalText = new();
    private CaseChange _caseChangeState = CaseChange.None;

    /// <summary>
    /// Creates the replacement text.
    /// </summary>
    /// <param name="sre">The regular expression (must be <see cref="Spcre.IsValid"/>).</param>
    /// <param name="text">The matched text used as the source of backreferences.</param>
    /// <param name="captureGroupsOffsets">
    /// Offsets of the capture groups relative to the start of <paramref name="text"/>
    /// (as in <see cref="Spcre.MatchInfo.CaptureGroupsOffsets"/>).
    /// </param>
    /// <param name="replacementPattern">Replacement pattern (text and/or control sequences).</param>
    /// <param name="result">The resulting replacement text.</param>
    /// <returns><c>true</c> when the replacement text was created.</returns>
    public bool BuildReplacementText(
        Spcre sre,
        string text,
        IReadOnlyList<SpcreCapture> captureGroupsOffsets,
        string replacementPattern,
        out string result)
    {
        ResetState();
        result = string.Empty;

        if (sre is null || !sre.IsValid)
        {
            return false;
        }

        replacementPattern ??= string.Empty;

        // Fast path: without '\' there are no control sequences.
        if (!replacementPattern.Contains('\\'))
        {
            result = replacementPattern;
            return true;
        }

        char controlChar = '\0';
        char backrefBracketStartChar = '\0';
        var backrefName = new StringBuilder();
        var invalidControl = new StringBuilder();
        var controlXHex = new StringBuilder();
        var controlX6Hex = new StringBuilder();
        bool inHex6 = false;
        bool inControl = false;

        for (int i = 0; i < replacementPattern.Length; i++)
        {
            char c = replacementPattern[i];

            if (inControl)
            {
                invalidControl.Append(c);

                if (controlChar == '\0')
                {
                    controlChar = c;

                    if (char.IsDigit(c))
                    {
                        int backrefNumber = c - '0';
                        if (backrefNumber >= 0 && backrefNumber < captureGroupsOffsets.Count)
                        {
                            AccumulateReplacementText(Substring(captureGroupsOffsets[backrefNumber], text));
                        }
                        else
                        {
                            AccumulateReplacementText(invalidControl.ToString());
                        }

                        inControl = false;
                    }
                    else if (c == 'a')
                    {
                        AccumulateReplacementText("\a");
                        inControl = false;
                    }
                    else if (c == 'b')
                    {
                        AccumulateReplacementText("\b");
                        inControl = false;
                    }
                    else if (c == 'f')
                    {
                        AccumulateReplacementText("\f");
                        inControl = false;
                    }
                    else if (c == 'n')
                    {
                        AccumulateReplacementText("\n");
                        inControl = false;
                    }
                    else if (c == 'r')
                    {
                        AccumulateReplacementText("\r");
                        inControl = false;
                    }
                    else if (c == 't')
                    {
                        AccumulateReplacementText("\t");
                        inControl = false;
                    }
                    else if (c == 'v')
                    {
                        AccumulateReplacementText("\v");
                        inControl = false;
                    }
                    else if (c == '\\')
                    {
                        AccumulateReplacementText("\\");
                        inControl = false;
                    }
                    else if (c == 'E')
                    {
                        _caseChangeState = CaseChange.None;
                        inControl = false;
                    }
                    else if (c == 'g')
                    {
                        backrefBracketStartChar = '\0';
                    }
                    else if (c == 'l')
                    {
                        TrySetCaseChange(CaseChange.LowerNext);
                        inControl = false;
                    }
                    else if (c == 'L')
                    {
                        TrySetCaseChange(CaseChange.Lower);
                        inControl = false;
                    }
                    else if (c == 'u')
                    {
                        TrySetCaseChange(CaseChange.UpperNext);
                        inControl = false;
                    }
                    else if (c == 'U')
                    {
                        TrySetCaseChange(CaseChange.Upper);
                        inControl = false;
                    }

                    // 'x' and unknown characters: stay in the control sequence, handled in later iterations.
                }
                else if (controlChar == 'g')
                {
                    if (backrefBracketStartChar == '\0')
                    {
                        if (c == '{' || c == '<')
                        {
                            backrefBracketStartChar = c;
                            backrefName.Clear();
                        }
                        else
                        {
                            inControl = false;
                            AccumulateReplacementText(invalidControl.ToString());
                        }
                    }
                    else if ((c == '}' && backrefBracketStartChar == '{') ||
                             (c == '>' && backrefBracketStartChar == '<'))
                    {
                        string name = backrefName.ToString();
                        if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int backrefNumber))
                        {
                            backrefNumber = 0;
                        }

                        if (backrefNumber == 0 && name != "0")
                        {
                            backrefNumber = sre.GetCaptureStringNumber(name);
                        }

                        if (backrefNumber >= 0 && backrefNumber < captureGroupsOffsets.Count)
                        {
                            AccumulateReplacementText(Substring(captureGroupsOffsets[backrefNumber], text));
                        }
                        else
                        {
                            AccumulateReplacementText(invalidControl.ToString());
                        }

                        inControl = false;
                    }
                    else
                    {
                        backrefName.Append(c);
                    }
                }
                else if (controlChar == 'x')
                {
                    if (c == '{' && !inHex6)
                    {
                        inHex6 = true;
                    }
                    else if (c == '}' && inHex6 && IsValidHex6(controlX6Hex.ToString()))
                    {
                        string hex = controlX6Hex.ToString();
                        if (hex.Length == 2 || hex.Length == 4)
                        {
                            AccumulateReplacementText(((char)Convert.ToInt32(hex, 16)).ToString());
                        }
                        else
                        {
                            int plane = Convert.ToInt32(hex.Substring(0, 2), 16);
                            int remainder = Convert.ToInt32(hex.Substring(hex.Length - 4), 16);
                            int codePoint = (65536 * plane) + remainder;
                            AccumulateReplacementText(ToUtf16(codePoint, invalidControl.ToString()));
                        }

                        inControl = false;
                        inHex6 = false;
                        controlX6Hex.Clear();
                    }
                    else if (IsHex(c))
                    {
                        if (inHex6)
                        {
                            controlX6Hex.Append(c);
                        }
                        else
                        {
                            controlXHex.Append(c);
                            if (controlXHex.Length == 2)
                            {
                                AccumulateReplacementText(((char)Convert.ToInt32(controlXHex.ToString(), 16)).ToString());
                                inControl = false;
                            }
                        }
                    }
                    else
                    {
                        AccumulateReplacementText(invalidControl.ToString());
                        inControl = false;
                    }
                }
                else
                {
                    AccumulateReplacementText(invalidControl.ToString());
                    inControl = false;
                }
            }
            else if (c == '\\')
            {
                invalidControl.Clear().Append(c);
                controlChar = '\0';
                controlXHex.Clear();
                controlX6Hex.Clear();
                inHex6 = false;
                inControl = true;
            }
            else
            {
                AccumulateReplacementText(c.ToString());
            }
        }

        // Unterminated control sequence at the end of the pattern — insert it literally.
        if (inControl)
        {
            AccumulateReplacementText(invalidControl.ToString());
        }

        result = _finalText.ToString();
        return true;
    }

    private static string ToUtf16(int codePoint, string fallback)
    {
        if (codePoint < 0 || codePoint > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
        {
            return fallback;
        }

        return char.ConvertFromUtf32(codePoint);
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static bool IsValidHex6(string hv)
    {
        int hl = hv.Length;
        if (hl < 2 || hl == 3 || hl == 5 || hl > 6)
        {
            return false;
        }

        foreach (char ch in hv)
        {
            if (!IsHex(ch))
            {
                return false;
            }
        }

        if (hl == 2 || hl == 4)
        {
            return true;
        }

        // hl == 6: restrict to valid code points (<= 0x10FFFF).
        return hv[0] == '0' || hv.StartsWith("10", StringComparison.Ordinal);
    }

    private static string Substring(SpcreCapture capture, string text)
    {
        int start = capture.Start;
        int end = capture.End;

        if (start < 0 || end < 0 || start > end || start > text.Length)
        {
            return string.Empty;
        }

        if (end > text.Length)
        {
            end = text.Length;
        }

        return text.Substring(start, end - start);
    }

    private void AccumulateReplacementText(string text) =>
        _finalText.Append(ProcessTextSegment(text));

    private string ProcessTextSegment(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        switch (_caseChangeState)
        {
            case CaseChange.LowerNext:
            {
                string processed = char.ToLowerInvariant(text[0]) + text.Substring(1);
                _caseChangeState = CaseChange.None;
                return processed;
            }

            case CaseChange.Lower:
                return text.ToLowerInvariant();

            case CaseChange.UpperNext:
            {
                string processed = char.ToUpperInvariant(text[0]) + text.Substring(1);
                _caseChangeState = CaseChange.None;
                return processed;
            }

            case CaseChange.Upper:
                return text.ToUpperInvariant();

            default:
                return text;
        }
    }

    private void TrySetCaseChange(CaseChange state)
    {
        if (_caseChangeState == CaseChange.None)
        {
            _caseChangeState = state;
        }
    }

    private void ResetState()
    {
        _finalText.Clear();
        _caseChangeState = CaseChange.None;
    }
}
