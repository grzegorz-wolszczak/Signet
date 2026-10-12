using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Resources;

namespace Signet.App.Views;

/// <summary>
/// The font workflows shared by the Book Browser and "Manage Fonts": picking font files and offering
/// <c>@font-face</c> rules for them, and deleting files with the font removal window when fonts among them are used.
/// </summary>
public static class FontDialogs
{
    private static readonly string[] FontPatterns = { "*.ttf", "*.otf", "*.woff", "*.woff2", "*.ttc" };

    /// <summary>
    /// Picks font files on disk, adds them to the book and shows the "Add @font-face rules" dialog for them.
    /// Returns whether anything was added.
    /// </summary>
    public static async Task<bool> PickAndAddFontsAsync(Window owner, IFontManagementHost host)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(host);
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("FontDialogs_PickTitle"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Strings.Get("FontDialogs_FontFiles")) { Patterns = FontPatterns },
            },
        });

        List<string> paths = files
            .Select(f => f.TryGetLocalPath() ?? f.Path.LocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
        if (paths.Count == 0)
        {
            return false;
        }

        IReadOnlyList<Resource> added = host.AddFiles(paths);
        await OfferFontFacesAsync(owner, host, added.OfType<FontResource>().ToList());
        return added.Count > 0;
    }

    /// <summary>
    /// Shows the "Add @font-face rules" dialog for <paramref name="fonts"/> (nothing without fonts) and writes the
    /// chosen rules; the stylesheet written to is opened. Returns whether rules were written.
    /// </summary>
    public static async Task<bool> OfferFontFacesAsync(Window owner, IFontManagementHost host, IReadOnlyList<FontResource> fonts)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(fonts);
        if (host.PrepareFontFaces(fonts) is not { } dialog)
        {
            return false;
        }

        FontFaceDialogResult? result = await FontFaceWindow.ShowAsync(owner, dialog);
        if (result is null || host.ApplyFontFaces(result) is not { } stylesheet)
        {
            return false;
        }

        host.OpenResources(new Resource[] { stylesheet });
        return true;
    }

    /// <summary>
    /// Deletes <paramref name="resources"/> after confirmation: with the font removal window when fonts among them
    /// are used by the stylesheets, otherwise with the plain confirmation. Returns whether the files were deleted.
    /// </summary>
    public static async Task<bool> DeleteAsync(Window owner, IFontManagementHost host, IReadOnlyList<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Count == 0)
        {
            return false;
        }

        if (host.PrepareFontRemoval(resources) is { } removal)
        {
            if (!await FontRemovalWindow.ShowAsync(owner, removal))
            {
                return false;
            }

            host.DeleteResources(resources, removal.Plan, removal.ExcludedKeys());
            return true;
        }

        string names = string.Join("\n", resources.Select(r => "  • " + r.Filename));
        if (!await ConfirmWindow.AskAsync(
                owner,
                Strings.Get("Common_Delete"),
                Strings.Format("BookBrowser_DeleteConfirm", resources.Count, names),
                Strings.Get("Common_Delete"),
                destructive: true))
        {
            return false;
        }

        host.DeleteResources(resources);
        return true;
    }
}
