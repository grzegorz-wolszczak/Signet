using System;
using System.Collections.Generic;
using System.Text;
using Signet.Core.Resources;

namespace Signet.Core.Misc;

/// <summary>
/// Generates the content of <c>META-INF/encryption.xml</c> for fonts marked for obfuscation.
/// </summary>
/// <remarks>
/// For each <see cref="FontResource"/> with a non-empty <see cref="FontResource.ObfuscationAlgorithm"/>
/// a single <c>&lt;enc:EncryptedData&gt;</c> block is written with the algorithm and a reference to the file
/// (<c>URI</c> = the font's bookpath). Fonts without an algorithm
/// are skipped. The block order follows the order of the passed resources.
/// </remarks>
public static class EncryptionXmlWriter
{
    private const string ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";
    private const string EncNs = "http://www.w3.org/2001/04/xmlenc#";

    /// <summary>Builds the <c>encryption.xml</c> content for the given fonts.</summary>
    /// <param name="fontResources">The book's font resources (only the obfuscated ones are processed).</param>
    /// <returns>A complete XML document (UTF-8, <c>\n</c> line endings).</returns>
    public static string WriteXml(IEnumerable<FontResource> fontResources)
    {
        ArgumentNullException.ThrowIfNull(fontResources);

        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<encryption xmlns=\"").Append(ContainerNs).Append("\" xmlns:enc=\"").Append(EncNs).Append("\">\n");

        foreach (FontResource font in fontResources)
        {
            string algorithm = font.ObfuscationAlgorithm;
            if (string.IsNullOrEmpty(algorithm))
            {
                continue;
            }

            sb.Append("  <enc:EncryptedData>\n");
            sb.Append("    <enc:EncryptionMethod Algorithm=\"").Append(Escape(algorithm)).Append("\"/>\n");
            sb.Append("    <enc:CipherData>\n");
            sb.Append("      <enc:CipherReference URI=\"").Append(Escape(font.BookPath)).Append("\"/>\n");
            sb.Append("    </enc:CipherData>\n");
            sb.Append("  </enc:EncryptedData>\n");
        }

        sb.Append("</encryption>");
        return sb.ToString();
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
