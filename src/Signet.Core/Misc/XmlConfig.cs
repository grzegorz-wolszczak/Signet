using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Signet.Core.Misc;

/// <summary>
/// Reading and writing of Signet's XML configuration files (settings, clips, saved searches, …).
/// </summary>
/// <remarks>
/// Reading is lenient: a missing, empty or malformed file yields <see langword="null"/> (no exception). Whitespace is
/// preserved, so a text value made only of spaces or line breaks reads back unchanged. Writing is atomic (temporary
/// file + <see cref="File.Move(string, string, bool)"/>), UTF-8 without BOM, indented; carriage returns are written as
/// character references, because an XML parser turns a literal <c>\r\n</c> into <c>\n</c>. Characters XML does not
/// allow (e.g. control characters) are not checked, so a stray one does not make saving fail.
/// </remarks>
public static class XmlConfig
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CheckCharacters = false,
    };

    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        IndentChars = "  ",
        NewLineHandling = NewLineHandling.Entitize,
        CheckCharacters = false,
    };

    /// <summary>Parses XML text; <see langword="null"/> when it is empty or malformed.</summary>
    public static XDocument? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using StringReader stringReader = new(text);
            using XmlReader reader = XmlReader.Create(stringReader, ReaderSettings);
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Loads an XML file; <see langword="null"/> when it is missing, unreadable, empty or malformed.</summary>
    public static XDocument? TryLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return File.Exists(path) ? TryParse(File.ReadAllText(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The document as text (with the XML declaration), as <see cref="Save"/> writes it.</summary>
    public static string ToText(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using MemoryStream stream = new();
        using (XmlWriter writer = XmlWriter.Create(stream, WriterSettings))
        {
            document.Save(writer);
        }

        return WriterSettings.Encoding.GetString(stream.ToArray());
    }

    /// <summary>Writes the document to a file atomically (creating the folder when needed).</summary>
    public static void Save(string path, XDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, ToText(document), WriterSettings.Encoding);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>The value of an attribute, or an empty string when it is missing.</summary>
    public static string Attribute(XElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Attribute(name)?.Value ?? string.Empty;
    }

    /// <summary>The text of a child element, or an empty string when it is missing.</summary>
    public static string ChildText(XElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Element(name)?.Value ?? string.Empty;
    }
}
