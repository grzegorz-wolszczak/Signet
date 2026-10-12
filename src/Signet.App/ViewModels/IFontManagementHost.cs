using System.Collections.Generic;
using Signet.Core.Fonts;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// The font operations on the open book that the font dialogs use (implemented by <see cref="BookBrowserViewModel"/>,
/// which owns the checkpoints and the tree refresh): adding font files and <c>@font-face</c> rules, deleting fonts
/// with the stylesheet cleanup, renaming a family, and the data for "Manage Fonts".
/// </summary>
public interface IFontManagementHost
{
    /// <summary>The fonts and families of the book (the open tabs are saved first), or <c>null</c> without a book.</summary>
    FontManagerReport? BuildFontReport();

    /// <summary>Copies files from disk into the book; returns the new resources.</summary>
    IReadOnlyList<Resource> AddFiles(IReadOnlyList<string> filePaths);

    /// <summary>The "Add @font-face rules" dialog for <paramref name="fonts"/>, or <c>null</c> without a book or fonts.</summary>
    FontFaceDialogViewModel? PrepareFontFaces(IReadOnlyList<FontResource> fonts);

    /// <summary>Writes the rules chosen in the dialog; returns the stylesheet written to, or <c>null</c> on failure.</summary>
    CssResource? ApplyFontFaces(FontFaceDialogResult result);

    /// <summary>
    /// The window to show before <paramref name="resources"/> are deleted when fonts among them are used by the
    /// stylesheets, or <c>null</c> when a plain confirmation is enough.
    /// </summary>
    FontRemovalViewModel? PrepareFontRemoval(IReadOnlyList<Resource> resources);

    /// <summary>
    /// Deletes <paramref name="resources"/>; with a <paramref name="plan"/>, its changes (except
    /// <paramref name="excludedKeys"/>) are applied to the stylesheets first — all under one checkpoint.
    /// </summary>
    void DeleteResources(IReadOnlyList<Resource> resources, FontRemovalPlan? plan = null, IReadOnlySet<string>? excludedKeys = null);

    /// <summary>Renames a family in the whole book; returns whether anything changed.</summary>
    bool RenameFontFamily(string oldFamily, string newFamily);

    /// <summary>Opens the resources in tabs.</summary>
    void OpenResources(IReadOnlyList<Resource> resources);
}
