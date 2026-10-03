using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Signet.Core.Localization;

namespace Signet.Core;

/// <summary>
/// Stateless helper functions.
/// </summary>
public static class Utility
{
    /// <summary>The prefix of temporary file names created by the application.</summary>
    public const string TemporaryFilePrefix = "signet_";

    /// <summary>
    /// Replaces the five predefined XML entities back with characters.
    /// The replacement order matters —
    /// <c>&amp;amp;</c> goes last so as not to expand twice.
    /// </summary>
    /// <param name="text">The text with entities.</param>
    /// <returns>The text with entities expanded.</returns>
    public static string DecodeXml(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text
            .Replace("&apos;", "'", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);
    }

    /// <summary>
    /// Escapes text into a safe XML form:
    /// first <see cref="DecodeXml"/> (so as not to double existing entities), then
    /// replacement of the metacharacters <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> - like
    /// <c>ToHtmlEscaped()</c> (the apostrophe is not encoded).
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <returns>The text with the metacharacters encoded.</returns>
    public static string EncodeXml(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return DecodeXml(text)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    /// <summary>
    /// A new UUID in canonical form without braces, in lowercase.
    /// Used where the EPUB standard
    /// requires a UUID (e.g. <c>dc:identifier</c>) - <em>without</em> the <c>urn:uuid:</c> prefix,
    /// which callers add. For internal identifiers we use
    /// <see cref="Ulid"/> (see CLAUDE.md).
    /// </summary>
    /// <returns>E.g. <c>1b4e28ba-2fa1-11d2-883f-0016d3cca427</c>.</returns>
    public static string CreateUuid()
    {
        return Guid.NewGuid().ToString("D");
    }

    /// <summary>
    /// Builds a temporary file path under the system temporary directory.
    /// The name is unique thanks to <see cref="Ulid"/>. <b>The file is not created</b> - managing
    /// the working directory is handled by <see cref="TempFolder"/>.
    /// </summary>
    /// <param name="extension">The extension with a leading dot (e.g. <c>.xhtml</c>) or <c>""</c>.</param>
    /// <returns>The full path of the temporary file.</returns>
    public static string GetTemporaryFileName(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        string fileName = TemporaryFilePrefix + Ulid.NewUlid().ToString() + extension;
        return Path.Combine(Path.GetTempPath(), fileName);
    }

    // SHA-1 and MD5 are required here by the EPUB font obfuscation specifications
    // (IDPF: SHA-1 of the unique identifier; Adobe: a key derived from MD5) and
    // for comparing/identifying content - they are not used for security purposes.
#pragma warning disable CA5350 // SHA-1 - wymagane przez algorytm obfuskacji IDPF
#pragma warning disable CA5351 // MD5 - wymagane przez algorytm obfuskacji Adobe

    /// <summary>The SHA-1 hash of the data, as lowercase hex (40 characters). Used e.g. for IDPF font obfuscation.</summary>
    public static string Sha1Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA1.HashData(data));

    /// <summary>The SHA-1 hash of a stream (read to the end), as lowercase hex.</summary>
    public static string Sha1Hex(Stream data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Convert.ToHexStringLower(SHA1.HashData(data));
    }

    /// <summary>The SHA-1 hash of UTF-8 encoded text, as lowercase hex.</summary>
    public static string Sha1Hex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Sha1Hex(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>The MD5 hash of the data, as lowercase hex (32 characters). Used e.g. for Adobe font obfuscation.</summary>
    public static string Md5Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(MD5.HashData(data));

    /// <summary>The MD5 hash of a stream (read to the end), as lowercase hex.</summary>
    public static string Md5Hex(Stream data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Convert.ToHexStringLower(MD5.HashData(data));
    }

    /// <summary>The MD5 hash of UTF-8 encoded text, as lowercase hex.</summary>
    public static string Md5Hex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Md5Hex(Encoding.UTF8.GetBytes(text));
    }

#pragma warning restore CA5351
#pragma warning restore CA5350

    /// <summary>The SHA-256 hash of the data, as lowercase hex (64 characters).</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>The SHA-256 hash of a stream (read to the end), as lowercase hex.</summary>
    public static string Sha256Hex(Stream data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Convert.ToHexStringLower(SHA256.HashData(data));
    }

    /// <summary>The SHA-256 hash of UTF-8 encoded text, as lowercase hex.</summary>
    public static string Sha256Hex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Sha256Hex(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>UTF-8 without a BOM - the standard encoding for writing all text files of the book.</summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Normalizes text to NFC and converts Windows (<c>\r\n</c>) and Mac (<c>\r</c>) line endings
    /// to <c>\n</c>.
    /// </summary>
    public static string ConvertLineEndingsAndNormalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Normalize(NormalizationForm.FormC)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a (non-HTML) text file as Unicode: detects a UTF-8/UTF-16/UTF-32 BOM,
    /// assumes UTF-8 when there is none. The result is normalized (NFC, <c>\n</c> line endings).
    /// </summary>
    /// <exception cref="IOException">When the file cannot be read.</exception>
    public static string ReadUnicodeTextFile(string fullFilePath)
    {
        ArgumentNullException.ThrowIfNull(fullFilePath);
        using StreamReader reader = new(fullFilePath, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        return ConvertLineEndingsAndNormalize(reader.ReadToEnd());
    }

    /// <summary>
    /// Writes text to a file as UTF-8 <b>without a BOM</b>, after normalizing to NFC and converting
    /// line endings to <c>\n</c> (normalizing line endings is deliberate — EPUB files should be
    /// deterministic on every platform).
    /// An existing file is overwritten.
    /// </summary>
    public static void WriteUnicodeTextFile(string text, string fullFilePath)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fullFilePath);
        File.WriteAllText(fullFilePath, ConvertLineEndingsAndNormalize(text), Utf8NoBom);
    }

    // Characters allowed in a URL path without percent-encoding.
    private const string UrlPathSafeChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_.-/~";

    /// <summary>
    /// Decodes a URL path: first an XML decode
    /// (in case of badly built hrefs), then a percent-decode in UTF-8, finally NFC.
    /// Intended for paths, not for full URLs with a fragment/scheme.
    /// </summary>
    /// <param name="path">The path, possibly percent-encoded.</param>
    /// <returns>The decoded path, normalized to NFC.</returns>
    public static string UrlDecodePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string decoded = Uri.UnescapeDataString(DecodeXml(path));
        return decoded.Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Encodes a URL path: XML-decode, NFC,
    /// stripping of any existing percent-encoding, then encoding every character
    /// outside the safe set as <c>%XX</c> (UTF-8 bytes, uppercase letters).
    /// </summary>
    /// <param name="path">The path to encode.</param>
    /// <returns>The path in URL-encoded form.</returns>
    public static string UrlEncodePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string work = DecodeXml(path).Normalize(NormalizationForm.FormC);
        work = Uri.UnescapeDataString(work);

        StringBuilder result = new(work.Length);
        Span<byte> utf8 = stackalloc byte[4];
        foreach (Rune rune in work.EnumerateRunes())
        {
            if (NeedToPercentEncode((uint)rune.Value))
            {
                int written = rune.EncodeToUtf8(utf8);
                for (int i = 0; i < written; i++)
                {
                    result.Append('%').Append(utf8[i].ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            else
            {
                result.Append(rune.ToString());
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Sorts a list of folders descending by their corresponding counts.
    /// Used when choosing the default
    /// folder of a group — the first element of the result is the folder with the most files of a given type.
    /// The sort is stable (ties keep the input order).
    /// </summary>
    /// <param name="folders">The list of folder paths.</param>
    /// <param name="counts">The counts, one per folder (the same length as <paramref name="folders"/>).</param>
    /// <returns>The folders ordered descending by count.</returns>
    public static IReadOnlyList<string> SortByCounts(IReadOnlyList<string> folders, IReadOnlyList<int> counts)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(counts);
        if (folders.Count != counts.Count)
        {
            throw new ArgumentException(CoreStrings.Format("Error_CountMismatch", "folders/counts"), nameof(counts));
        }

        return folders
            .Select((folder, index) => (folder, count: counts[index]))
            .OrderByDescending(pair => pair.count)
            .Select(pair => pair.folder)
            .ToList();
    }

    /// <summary>
    /// Generates an identifier of the form <c>{id}_{n}</c>, starting from <c>n=1</c> and incrementing until
    /// it hits a value absent from <paramref name="used"/>
    /// (used e.g. in Merge for injected section anchors).
    /// </summary>
    public static string GenerateUniqueId(string id, HashSet<string> used)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(used);
        int count = 1;
        string newId = $"{id}_{count}";
        while (used.Contains(newId))
        {
            count++;
            newId = $"{id}_{count}";
        }

        return newId;
    }

    /// <summary>
    /// Whether a code point needs percent-encoding in a URL path (the <c>iunreserved</c>
    /// set from RFC 3987).
    /// </summary>
    internal static bool NeedToPercentEncode(uint codePoint)
    {
        if (codePoint < 128)
        {
            return !UrlPathSafeChars.Contains((char)codePoint, StringComparison.Ordinal);
        }

        if (codePoint < 0xA0)
        {
            return true;
        }

        if (codePoint <= 0xD7FF)
        {
            return false;
        }

        if (codePoint < 0xF900)
        {
            return true;
        }

        if (codePoint <= 0xFDCF)
        {
            return false;
        }

        if (codePoint < 0xFDF0)
        {
            return true;
        }

        if (codePoint <= 0xFFEF)
        {
            return false;
        }

        if (codePoint < 0x10000)
        {
            return true;
        }

        if (codePoint <= 0x1FFFD)
        {
            return false;
        }

        if (codePoint < 0x20000)
        {
            return true;
        }

        if (codePoint <= 0x2FFFD)
        {
            return false;
        }

        if (codePoint < 0x30000)
        {
            return true;
        }

        return codePoint > 0x3FFFD;
    }

    /// <summary>
    /// Changes the letter case of text according to the chosen variant
    /// (used by "Change Case" in the Format menu). The title/capitalize algorithms are deliberately simple
    /// (the first letter after whitespace / the first letter overall).
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <param name="casing">The case change variant.</param>
    /// <returns>The text after the change; for <see cref="Casing.Lowercase"/> on empty input — unchanged.</returns>
    public static string ChangeCase(string text, Casing casing)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return text;
        }

        CultureInfo culture = CultureInfo.CurrentCulture;
        switch (casing)
        {
            case Casing.Lowercase:
                return text.ToLower(culture);

            case Casing.Uppercase:
                return text.ToUpper(culture);

            case Casing.Titlecase:
            {
                char[] chars = text.ToLower(culture).ToCharArray();
                int i = 0;
                while (i < chars.Length && char.IsWhiteSpace(chars[i]))
                {
                    i++;
                }

                while (i < chars.Length)
                {
                    if (i == 0 || char.IsWhiteSpace(chars[i - 1]))
                    {
                        chars[i] = char.ToUpper(chars[i], culture);
                    }

                    i++;
                }

                return new string(chars);
            }

            case Casing.Capitalize:
            {
                char[] chars = text.ToLower(culture).ToCharArray();
                int i = 0;
                while (i < chars.Length && char.IsWhiteSpace(chars[i]))
                {
                    i++;
                }

                if (i < chars.Length)
                {
                    chars[i] = char.ToUpper(chars[i], culture);
                }

                return new string(chars);
            }

            default:
                return text;
        }
    }
}

/// <summary>The case change variants for <see cref="Utility.ChangeCase"/>.</summary>
public enum Casing
{
    /// <summary>All letters lowercase.</summary>
    Lowercase,

    /// <summary>All letters uppercase.</summary>
    Uppercase,

    /// <summary>The first letter of each word uppercase, the rest lowercase.</summary>
    Titlecase,

    /// <summary>The first letter of the text uppercase, the rest lowercase.</summary>
    Capitalize,
}
