using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Signet.Core.Misc;

/// <summary>
/// Reading and writing the empty EPUB layout from the "Custom Epub Layout Designer" wizard — an XML list of book
/// paths:
/// <code>
/// &lt;empty_epub_layout&gt;
///   &lt;book_path&gt;OEBPS/content.opf&lt;/book_path&gt;
///   &lt;book_path&gt;OEBPS/Text/marker.xhtml&lt;/book_path&gt;
/// &lt;/empty_epub_layout&gt;
/// </code>
/// </summary>
public static class EmptyEpubLayout
{
    /// <summary>The default layout file name in the preferences folder.</summary>
    public const string DefaultLayoutFileName = "signet_empty_epub.xml";

    private const string RootElement = "empty_epub_layout";
    private const string BookPathElement = "book_path";

    /// <summary>The full path of the default layout file in the preferences folder.</summary>
    public static string DefaultLayoutFilePath => Path.Combine(AppDirectories.PrefsDirectory, DefaultLayoutFileName);

    /// <summary>Reads the bookpaths from a layout file; an empty list when the file does not exist or is invalid.</summary>
    public static IReadOnlyList<string> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (XmlConfig.TryLoad(path)?.Root is not { } root || root.Name != RootElement)
        {
            return Array.Empty<string>();
        }

        return root.Elements(BookPathElement)
            .Select(e => e.Value)
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>Reads the default layout from the preferences folder (an empty list when there is none).</summary>
    public static IReadOnlyList<string> ReadDefault() => Read(DefaultLayoutFilePath);

    /// <summary>Writes the bookpaths to a layout file (creating the folder if needed).</summary>
    public static void Write(string path, IReadOnlyList<string> bookPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bookPaths);
        XmlConfig.Save(path, new XDocument(new XElement(RootElement, bookPaths.Select(p => new XElement(BookPathElement, p)))));
    }

    /// <summary>Writes the given layout as the default one in the preferences folder.</summary>
    public static void WriteDefault(IReadOnlyList<string> bookPaths)
    {
        AppDirectories.EnsurePrefsDirectory();
        Write(DefaultLayoutFilePath, bookPaths);
    }
}
