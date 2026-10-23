using System;
using Signet.Core.BookManipulation;

namespace Signet.Core.Importers;

/// <summary>The part of loading an EPUB that is running (<see cref="BookLoadProgress.Stage"/>).</summary>
public enum BookLoadStage
{
    /// <summary>Unpacking the archive (<see cref="BookLoadProgress.Done"/> of <see cref="BookLoadProgress.Total"/> entries).</summary>
    Extracting,

    /// <summary>Reading the files of the manifest into the book (a file of the total).</summary>
    Reading,

    /// <summary>Reading and checking the (X)HTML files (a file of the total).</summary>
    Checking,

    /// <summary>The fonts, the navigation and the rest that has no count.</summary>
    Finishing,
}

/// <summary>The part of saving an EPUB that is running (<see cref="BookSaveProgress.Stage"/>).</summary>
public enum BookSaveStage
{
    /// <summary>Writing the resources to the working folder and stamping the metadata (no count).</summary>
    Preparing,

    /// <summary>Copying the files of the working folder to the staging folder (a file of the total).</summary>
    Copying,

    /// <summary>Writing the entries of the archive (an entry of the total).</summary>
    Archiving,
}

/// <summary>How far saving an EPUB (<see cref="ExportEpub.WriteBook"/>) is.</summary>
/// <param name="Stage">What it is doing.</param>
/// <param name="Done">How far within the stage.</param>
/// <param name="Total">The total for <paramref name="Done"/>; 0 when the stage has no count.</param>
public readonly record struct BookSaveProgress(BookSaveStage Stage, int Done, int Total);

/// <summary>How far loading an EPUB (<see cref="IImporter.GetBook"/>) is.</summary>
/// <param name="Stage">What it is doing.</param>
/// <param name="Done">How far within the stage.</param>
/// <param name="Total">The total for <paramref name="Done"/>; 0 when the stage has no count.</param>
public readonly record struct BookLoadProgress(BookLoadStage Stage, int Done, int Total);

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
    /// <param name="progress">Told how far the load is, from the thread that loads (only EPUB reports; <c>null</c> for nobody).</param>
    Book GetBook(bool extractMetadata = true, IProgress<BookLoadProgress>? progress = null);
}

/// <summary>
/// Settings passed to importers by the application layer (importers do not read
/// <c>SettingsStore</c> directly).
/// </summary>
/// <param name="DefaultVersion">Default EPUB version for newly created publications (<c>2.0</c> / <c>3.0</c>).</param>
/// <param name="MendOnOpen">Whether to repair (Mend) the HTML source on import.</param>
/// <param name="MendAddMissingDoctype">Whether that repair adds a DOCTYPE when the source has none.</param>
public readonly record struct ImporterOptions(string DefaultVersion, bool MendOnOpen, bool MendAddMissingDoctype = false)
{
    /// <summary>Default settings: EPUB 2, no repair on open.</summary>
    public static ImporterOptions Default { get; } = new("2.0", false);
}
