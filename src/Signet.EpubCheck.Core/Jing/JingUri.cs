using System;
using System.Globalization;
using System.Text;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Jing;

/// <summary>Jing's URI helpers (<c>com.thaiopensource.util.Uri</c> and <c>UriEncoder</c>).</summary>
internal static class JingUri
{
    private const char FragmentSeparator = '#';
    private const char SchemeSeparator = ':';
    private const char Space = ' ';
    private const char Delete = '\u007f';
    private const char FirstNonAscii = '\u0080';
    private const char LastC1Control = '\u009f';
    private const string HexDigits = "0123456789ABCDEF";
    private const int NibbleBits = 4;
    private const int NibbleMask = 0xF;

    // Which characters UriEncoder escapes: those java.net.URI rejects (encode), or all but ASCII graphic ones (encodeAsAscii).
    [Flags]
    private enum Forbidden
    {
        None = 0,
        C0Control = 0x01,
        Space = 0x02,
        Delim = 0x04,
        Unwise = 0x08,
        Delete = 0x10,
        C1Control = 0x20,
        NonAsciiSeparator = 0x40,
        OtherNonAscii = 0x80,
        AsciiControl = C0Control | Delete,
        Control = AsciiControl | C1Control,
        Separator = NonAsciiSeparator | Space,
        AsciiPrintable = Delim | Unwise | Space,
        Ascii = AsciiControl | AsciiPrintable,
        NonAscii = C1Control | NonAsciiSeparator | OtherNonAscii,
        JavaUri = Control | Separator | AsciiPrintable,
        Uri = Ascii | NonAscii,
    }

    /// <summary>Whether <paramref name="s"/> is a URI reference (after escaping what <c>java.net.URI</c> cannot take).</summary>
    public static bool IsValid(string s) => JavaUri.IsValid(Encode(s));

    /// <summary>Escapes every character that is not allowed in a URI (non-ASCII ones included).</summary>
    public static string EscapeDisallowedChars(string s) => Encode(s, Forbidden.Uri);

    /// <summary>
    /// <paramref name="uriReference"/> resolved against <paramref name="baseUri"/> when it is relative and the base is
    /// absolute; otherwise (or when either does not parse) the reference itself.
    /// </summary>
    public static string Resolve(string? baseUri, string uriReference)
    {
        if (!IsAbsolute(uriReference) && baseUri is not null && IsAbsolute(baseUri))
        {
            try
            {
                return JavaUri.Resolve(Encode(baseUri), Encode(uriReference));
            }
            catch (FormatException)
            {
                // Jing falls back to the reference.
            }
        }

        return uriReference;
    }

    /// <summary>Whether the URI has a fragment identifier.</summary>
    public static bool HasFragmentId(string uri) => uri.Contains(FragmentSeparator, StringComparison.Ordinal);

    /// <summary>Whether the URI has a scheme: a colon before any <c>#</c>, <c>/</c> or <c>?</c>.</summary>
    public static bool IsAbsolute(string uri)
    {
        int i = uri.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (i < 0)
        {
            return false;
        }

        while (--i >= 0)
        {
            if (uri[i] is FragmentSeparator or '/' or '?')
            {
                return false;
            }
        }

        return true;
    }

    // UriEncoder.encode: escapes what java.net.URI does not accept.
    private static string Encode(string s) => Encode(s, Forbidden.JavaUri);

    private static string Encode(string s, Forbidden flags)
    {
        StringBuilder? encoded = null;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (MustEncode(c, flags))
            {
                encoded ??= new StringBuilder(s, 0, i, s.Length);
                int codePoint = c;
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(c, s[++i]);
                }

                foreach (byte b in Utf8Encode(codePoint))
                {
                    encoded.Append('%').Append(HexDigits[(b >> NibbleBits) & NibbleMask]).Append(HexDigits[b & NibbleMask]);
                }
            }
            else
            {
                encoded?.Append(c);
            }
        }

        return encoded?.ToString() ?? s;
    }

    private static bool MustEncode(char c, Forbidden flags)
    {
        switch (c)
        {
            case '<' or '>' or '"':
                return flags.HasFlag(Forbidden.Delim);
            case '{' or '}' or '|' or '\\' or '^' or '`':
                return flags.HasFlag(Forbidden.Unwise);
            case Space:
                return flags.HasFlag(Forbidden.Space);
            case Delete:
                return flags.HasFlag(Forbidden.Delete);
        }

        if (c < Space)
        {
            return flags.HasFlag(Forbidden.C0Control);
        }

        if (c < FirstNonAscii)
        {
            return false;
        }

        Forbidden nonAscii = flags & Forbidden.NonAscii;
        if (nonAscii == Forbidden.NonAscii)
        {
            return true;
        }

        if (nonAscii == Forbidden.None)
        {
            return false;
        }

        if (c <= LastC1Control)
        {
            return flags.HasFlag(Forbidden.C1Control);
        }

        return IsSpaceChar(c) ? flags.HasFlag(Forbidden.NonAsciiSeparator) : flags.HasFlag(Forbidden.OtherNonAscii);
    }

    // Java's Character.isSpaceChar.
    private static bool IsSpaceChar(char c) => char.GetUnicodeCategory(c) is UnicodeCategory.SpaceSeparator
        or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    // Jing's Utf8.encode: also encodes a lone surrogate, as three bytes (Encoding.UTF8 would replace it).
    private static byte[] Utf8Encode(int c)
    {
        const int OneByteLimit = 0x80;
        const int TwoByteLimit = 0x800;
        const int ThreeByteLimit = 0x10000;
        const int ContinuationBits = 6;
        const int ContinuationMask = 0x3F;
        const int Continuation = 0x80;
        const int TwoByteLead = 0xC0;
        const int ThreeByteLead = 0xE0;
        const int FourByteLead = 0xF0;
        if (c < OneByteLimit)
        {
            return [(byte)c];
        }

        if (c < TwoByteLimit)
        {
            return [(byte)(TwoByteLead | (c >> ContinuationBits)), (byte)(Continuation | (c & ContinuationMask))];
        }

        if (c < ThreeByteLimit)
        {
            return
            [
                (byte)(ThreeByteLead | (c >> (2 * ContinuationBits))),
                (byte)(Continuation | ((c >> ContinuationBits) & ContinuationMask)),
                (byte)(Continuation | (c & ContinuationMask)),
            ];
        }

        return
        [
            (byte)(FourByteLead | (c >> (3 * ContinuationBits))),
            (byte)(Continuation | ((c >> (2 * ContinuationBits)) & ContinuationMask)),
            (byte)(Continuation | ((c >> ContinuationBits) & ContinuationMask)),
            (byte)(Continuation | (c & ContinuationMask)),
        ];
    }
}
