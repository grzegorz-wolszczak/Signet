using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Signet.Core.Misc;

/// <summary>
/// (De)obfuscation of font files embedded in an EPUB.
/// </summary>
/// <remarks>
/// <para>
/// Both methods — IDPF (<see cref="OcfReader.IdpfFontAlgorithmId"/>) and Adobe
/// (<see cref="OcfReader.AdobeFontAlgorithmId"/>) — XOR the leading bytes of the file with a key
/// derived from the publication identifier. The operation is an involution
/// (<c>f(f(x)) = x</c>), so the same function both obfuscates and deobfuscates.
/// </para>
/// <list type="bullet">
///   <item>IDPF: key = SHA-1 of the identifier with spaces, tabs, CR and LF removed
///     (Latin-1 bytes); XOR of the first 1040 bytes.</item>
///   <item>Adobe: key = hex-decoded identifier with <c>urn:uuid:</c>, <c>-</c> and <c>:</c>
///     removed; XOR of the first 1024 bytes.</item>
/// </list>
/// </remarks>
public static class FontObfuscation
{
    /// <summary>Number of leading file bytes XOR-ed by the Adobe method.</summary>
    public const int AdobeMethodNumBytes = 1024;

    /// <summary>Number of leading file bytes XOR-ed by the IDPF method.</summary>
    public const int IdpfMethodNumBytes = 1040;

    /// <summary>
    /// (De)obfuscates a font file in place, choosing the algorithm by <paramref name="algorithm"/>.
    /// </summary>
    /// <param name="filePath">Full path of the font file on disk.</param>
    /// <param name="algorithm">Algorithm identifier (IDPF or Adobe).</param>
    /// <param name="identifier">Publication identifier used to derive the key.</param>
    /// <exception cref="FontObfuscationException">
    /// The file does not exist, <paramref name="algorithm"/> or <paramref name="identifier"/> is empty,
    /// or the algorithm is unknown.
    /// </exception>
    public static void ObfuscateFile(string filePath, string algorithm, string identifier)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(identifier);

        if (!File.Exists(filePath) || algorithm.Length == 0 || identifier.Length == 0)
        {
            throw new FontObfuscationException($"{filePath}: {algorithm}: {identifier}");
        }

        byte[] key;
        int span;
        switch (algorithm)
        {
            case OcfReader.AdobeFontAlgorithmId:
                key = AdobeKeyFromIdentifier(identifier);
                span = AdobeMethodNumBytes;
                break;
            case OcfReader.IdpfFontAlgorithmId:
                key = IdpfKeyFromIdentifier(identifier);
                span = IdpfMethodNumBytes;
                break;
            default:
                throw new FontObfuscationException($"{filePath}: {algorithm}: {identifier}");
        }

        if (key.Length == 0)
        {
            // An empty key (e.g. the identifier is not a UUID) silently leaves the file unchanged.
            return;
        }

        byte[] contents = File.ReadAllBytes(filePath);
        int limit = Math.Min(span, contents.Length);
        for (int i = 0; i < limit; i++)
        {
            contents[i] ^= key[i % key.Length];
        }

        File.WriteAllBytes(filePath, contents);
    }

    /// <summary>IDPF key: SHA-1 of the identifier without whitespace (Latin-1 bytes).</summary>
    public static byte[] IdpfKeyFromIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        StringBuilder sb = new(identifier.Length);
        foreach (char c in identifier)
        {
            if (c is not (' ' or '\t' or '\r' or '\n'))
            {
                sb.Append(c);
            }
        }

#pragma warning disable CA5350 // SHA-1 is mandated by the IDPF font obfuscation algorithm (not used for security)
        return SHA1.HashData(Encoding.Latin1.GetBytes(sb.ToString()));
#pragma warning restore CA5350
    }

    /// <summary>Adobe key: hex-decoded identifier without <c>urn:uuid:</c>, <c>-</c>, <c>:</c>.</summary>
    public static byte[] AdobeKeyFromIdentifier(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        string cruftFree = identifier
            .Replace("urn:uuid:", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal);

        try
        {
            return Convert.FromHexString(cruftFree);
        }
        catch (FormatException)
        {
            // Invalid or odd-length hex yields an empty key, which leaves the file unchanged.
            return Array.Empty<byte>();
        }
    }
}

/// <summary>
/// Thrown when font (de)obfuscation cannot be performed (missing file, missing algorithm/identifier,
/// unknown algorithm).
/// </summary>
public sealed class FontObfuscationException : Exception
{
    /// <summary>Creates an exception without a message.</summary>
    public FontObfuscationException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public FontObfuscationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public FontObfuscationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
