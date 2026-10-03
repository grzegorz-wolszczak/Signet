using Signet.Core.BookManipulation;

namespace Signet.Core.Importers;

/// <summary>
/// Common interface of file importers into the <see cref="Book"/> model.
/// </summary>
public interface IImporter
{
    /// <summary>Full path of the imported file.</summary>
    string FullFilePath { get; }

    /// <summary>
    /// Checks whether the file can be loaded (well-formed check for HTML/XHTML).
    /// Returns <c>null</c> when there are no obstacles.
    /// </summary>
    WellFormedResult? CheckValidToLoad();

    /// <summary>Loads the file and returns the built publication model.</summary>
    /// <param name="extractMetadata">Whether to try to extract metadata from the source file.</param>
    Book GetBook(bool extractMetadata = true);
}

/// <summary>
/// Settings passed to importers by the application layer (importers do not read
/// <c>SettingsStore</c> directly).
/// </summary>
/// <param name="DefaultVersion">Default EPUB version for newly created publications (<c>2.0</c> / <c>3.0</c>).</param>
/// <param name="MendOnOpen">Whether to repair (Mend) the HTML source on import.</param>
public readonly record struct ImporterOptions(string DefaultVersion, bool MendOnOpen)
{
    /// <summary>Default settings: EPUB 2, no repair on open.</summary>
    public static ImporterOptions Default { get; } = new("2.0", false);
}
