using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Signet.Core.Resources;

/// <summary>
/// XML resource (OPF, NCX, XHTML — through subclasses). Adds reading with (X)HTML/XML encoding
/// detection and a well-formedness check.
/// </summary>
public class XmlResource : TextResource
{
    private const string ValidIdFirstChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string ValidIdChars = ValidIdFirstChars + "_-.0123456789";

    /// <inheritdoc cref="Resource(string, string)"/>
    public XmlResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Xml;

    private static readonly XmlReaderSettings WellFormedCheckSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        CheckCharacters = true,
    };

    /// <summary>
    /// Whether the current content is well-formed XML. A simplified check: the DTD declaration
    /// is ignored (not fetched) and the error location is not reported.
    /// </summary>
    public bool IsWellFormed()
    {
        string text = GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using StringReader stringReader = new(text);
            using XmlReader reader = XmlReader.Create(stringReader, WellFormedCheckSettings);
            while (reader.Read())
            {
                // read through the whole document — syntax errors throw XmlException
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    protected override string ReadTextFromDisk(string fullFilePath) =>
        HtmlEncodingResolver.ReadHtmlFile(fullFilePath);

    /// <summary>
    /// Creates a valid XML identifier from the given value: trims and collapses whitespace,
    /// transliterates to ASCII (<see cref="AsciiFy"/>), removes characters outside
    /// <c>[A-Za-z0-9_.-]</c>, replaces an empty result with a ULID, and prefixes the result with
    /// <c>x</c> if its first character is not a letter.
    /// </summary>
    public static string GetValidId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string simplified = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string transliterated = AsciiFy.ConvertToPlainAscii(simplified);

        StringBuilder sb = new(transliterated.Length);
        foreach (char c in transliterated)
        {
            if (IsValidIdCharacter(c))
            {
                sb.Append(c);
            }
        }

        string result = sb.ToString();
        if (result.Length == 0)
        {
            result = Ulid.NewUlid().ToString();
        }

        if (!ValidIdFirstChars.Contains(result[0], StringComparison.Ordinal))
        {
            result = "x" + result;
        }

        return result;
    }

    /// <summary>Whether the character may appear in an XML <c>id</c> attribute (simplified rule).</summary>
    public static bool IsValidIdCharacter(char character) =>
        ValidIdChars.Contains(character, StringComparison.Ordinal);
}
