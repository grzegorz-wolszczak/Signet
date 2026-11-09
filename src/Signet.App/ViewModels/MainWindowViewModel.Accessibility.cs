using System;
using System.Threading.Tasks;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// Tools → Accessibility…: the languages of the book and its files, the text alternatives of its images and the accessibility metadata
/// (<see cref="AccessibilityViewModel"/>), written in one step behind a checkpoint.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Raised by "Accessibility…" — the view calls <see cref="CreateAccessibilityViewModel"/> and shows the dialog.</summary>
    public event EventHandler? AccessibilityRequested;

    /// <summary>
    /// The "Accessibility" dialog for the open book (the open tabs are saved first), or <c>null</c> when no book is open.
    /// </summary>
    /// <param name="runWithProgress">Shows the progress of "Save" over the dialog (<c>null</c>: without a window).</param>
    public AccessibilityViewModel? CreateAccessibilityViewModel(ProgressRunner? runWithProgress = null)
    {
        if (_currentBook is not { } book)
        {
            return null;
        }

        _tabManager.SaveAllTabs();
        return new AccessibilityViewModel(book, NavigateToBookPathAtOffset, request => SaveAccessibilityAsync(book, request, runWithProgress));
    }

    /// <summary>
    /// "Save" in the dialog: the languages, the descriptions of the images and the metadata are written behind one checkpoint off the UI
    /// thread, and the tabs and panels are refreshed. Returns whether it ran.
    /// </summary>
    public async Task<bool> SaveAccessibilityAsync(Book book, AccessibilitySaveRequest request, ProgressRunner? runWithProgress = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(book, _currentBook) || request.IsEmpty)
        {
            return false;
        }

        _tabManager.SaveAllTabs();
        string title = ActionLabel(AppActionIds.Accessibility);
        BookOperationOutcome<int> run = await RunBookOperationAsync(
            title, title, editsBook: true,
            (_, _) => WriteAccessibility(book, request),
            runWithProgress,
            interruptible: false)
            .ConfigureAwait(true);
        if (!run.Completed)
        {
            return false;
        }

        RefreshAfterMaintenanceOperation();
        _statusBar.ShowMessage(Strings.Get("Status_AccessibilitySaved"), TimeSpan.FromSeconds(4));
        return true;
    }

    private static int WriteAccessibility(Book book, AccessibilitySaveRequest request)
    {
        int changed = 0;
        if (request.Languages is not null)
        {
            AccessibilityLanguage.SetLanguages(book, request.Languages);
            changed++;
        }

        if (request.HtmlLanguage != HtmlLanguageChoice.Keep)
        {
            changed += AccessibilityLanguage.SetHtmlLanguages(book, onlyMissing: request.HtmlLanguage == HtmlLanguageChoice.OnlyMissing);
        }

        if (request.ImageEdits.Count > 0)
        {
            changed += AccessibilityImages.Apply(book, request.ImageEdits);
        }

        if (request.Metadata is not null)
        {
            AccessibilityMetadataEditor.Write(book, request.Metadata);
            changed++;
        }

        return changed;
    }
}
