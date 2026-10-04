using System.Threading.Tasks;
using Signet.Core.BookManipulation;
using Signet.Core.MiscEditors;
using Signet.Core.Resources;

namespace Signet.App.ViewModels.Tabs;

/// <summary>
/// Main window services needed by the Code View context menu (the editor reaches the
/// main window for clips, actions, book operations and the image preview). Implemented by
/// <c>MainWindowViewModel</c>; <c>TabManager</c> passes it to every new code tab.
/// </summary>
public interface ICodeTabHost
{
    /// <summary>Root of the clip library (the "Clips" submenu).</summary>
    ClipEditorNode ClipLibraryRoot { get; }

    /// <summary>Pastes the clip content into the active document.</summary>
    void PasteClip(string text);

    /// <summary>"Add To Clips..." — adds the text as a new clip and opens the Clip Editor.</summary>
    void AddToClips(string text);

    /// <summary>Runs the application action with the given id (e.g. Go To Link Or Style, Mark Selection).</summary>
    void ExecuteAction(string actionId);

    /// <summary>
    /// Formats the text of an (X)HTML file editor: <paramref name="toValid"/> = Mend Code, otherwise
    /// Mend and Prettify Code. <c>null</c> when the operation was cancelled (e.g. the file is not
    /// well-formed, or the user cancelled the missing DOCTYPE warning).
    /// </summary>
    Task<string?> ReformatHtmlTextAsync(Resource resource, string text, bool toValid);

    /// <summary>
    /// "Rename Class" from an XHTML file: saves the tabs and returns a
    /// <see cref="ClassRenamer"/> over the texts of the whole book. <c>null</c> (with a message on the
    /// status bar) when there is no book or any XHTML file is not well-formed, and also when the user
    /// cancelled the missing DOCTYPE warning.
    /// </summary>
    Task<ClassRenamer?> PrepareClassRenameAsync();

    /// <summary>Writes the result of "Rename Class" into the book files and refreshes the tabs, Book Browser and Preview.</summary>
    void ApplyClassRename(ClassRenameResult result);

    /// <summary>"View Image" — shows the image with the given book path in the preview window.</summary>
    void ViewImage(string bookPath);
}
