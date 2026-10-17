using System;
using System.Linq;
using Signet.App.ViewModels.Tabs;
using Signet.Core.MainUI;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>Appearance › Code Editor: the sample tab of the preview.</summary>
public sealed partial class PreferencesViewModel
{
    private TabManagerModel? _codeEditorSampleTabs;
    private CodeTabViewModel? _codeEditorSample;

    /// <summary>
    /// The tab of the preview of the Code Editor pages: the real Code Editor tab over a chapter of the sample book, reading the
    /// look from the settings of this window (the draft), so every change shows in it at once. Made when first asked for.
    /// </summary>
    public CodeTabViewModel CodeEditorSample => _codeEditorSample ??= CreateCodeEditorSample();

    private CodeTabViewModel CreateCodeEditorSample()
    {
        Core.BookManipulation.Book book = SampleBookInMemory;
        HtmlResource chapter = book.GetFolderKeeper().GetResourceTypeList<HtmlResource>().First(h => h.Filename == "Chapter01.xhtml");
        _codeEditorSampleTabs = new TabManagerModel();
        CodeTabViewModel tab = new(_codeEditorSampleTabs.OpenResource(chapter), SampleStatusBar, _settings, _spellChecker, isSample: true)
        {
            BookPathExists = path => book.GetFolderKeeper().GetResourceByBookPathNoThrow(path) is not null,
        };

        // The caret rests in the opening tag of the first paragraph: the line of the caret, its number and the pair of
        // tags show; a word of the text is a match of a search. (The view puts the caret there when it is shown.)
        string text = tab.Document.Text;
        int caret = text.IndexOf("<p class=\"lead\"", StringComparison.Ordinal) + 4;
        int keeper = text.IndexOf("keeper", StringComparison.Ordinal);
        tab.ViewState = new CodeTabViewState(1, caret, caret, 0);
        tab.SetSearchHighlights(new[] { (keeper, keeper + "keeper".Length) });
        return tab;
    }
}
