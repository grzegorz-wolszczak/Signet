namespace Signet.App.Services;

/// <summary>
/// The open book as seen by Preferences → Edition page: whether it has an edition page and removing it
/// (<see cref="Signet.Core.BookManipulation.EditionPage"/>). Implemented by the main window.
/// </summary>
public interface IEditionPageHost
{
    /// <summary>Whether a book is open.</summary>
    bool HasOpenBook { get; }

    /// <summary>The bookpath of the edition page in the open book; <c>null</c> when there is none (or no book).</summary>
    string? EditionPageBookPath { get; }

    /// <summary>Removes the edition page from the open book (with its TOC entry). Returns whether it was removed.</summary>
    bool RemoveEditionPage();
}
