using System;
using System.IO;
using Signet.Core.BookManipulation;

namespace Signet.Core.Importers;

/// <summary>
/// Picks an importer based on the file extension.
/// Supported formats: <c>.epub</c>, <c>.xhtml</c>/<c>.html</c>/<c>.htm</c>, <c>.txt</c>.
/// </summary>
public static class ImporterFactory
{
    /// <summary>Extensions of files that can be loaded (without the dot, lowercase).</summary>
    public static readonly string[] SupportedExtensions = { "epub", "xhtml", "html", "htm", "txt" };

    /// <summary>
    /// Returns the importer for the given file, or <c>null</c> when the format is not supported.
    /// </summary>
    /// <param name="filename">Path of the input file.</param>
    /// <param name="options">Import settings passed to the HTML/TXT importers.</param>
    public static IImporter? GetImporter(string filename, ImporterOptions options = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        string extension = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();

        return extension switch
        {
            "epub" => new ImportEpub(filename),
            "xhtml" or "html" or "htm" => new ImportHtml(filename, options),
            "txt" => new ImportTxt(filename, options),
            _ => null,
        };
    }

    /// <summary>Whether a file with this path has a supported extension.</summary>
    public static bool IsSupported(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return false;
        }

        string extension = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
        return Array.IndexOf(SupportedExtensions, extension) >= 0;
    }
}
