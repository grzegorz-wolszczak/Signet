using System.Collections.Generic;
using System.Globalization;

namespace Signet.App.Actions;

/// <summary>
/// Catalog of all main window actions: text, default shortcut, icon and category.
/// Panel shortcuts (Alt+F1 etc.) are listed here as well.
/// <para>
/// <b>Shortcuts and AltGr.</b> On Windows AltGr is reported as Ctrl+Alt, so a
/// <c>Ctrl+Alt+&lt;letter&gt;</c> shortcut swallows the character typed with AltGr+&lt;letter&gt;,
/// making it impossible to type in the editor or in text boxes. Shortcuts that would clash with
/// Polish characters (ą ć ł ń) are therefore kept off Ctrl+Alt:
/// Replace All <c>Ctrl+Shift+A</c>, Count All <c>Ctrl+Shift+N</c>, Clip Editor
/// <c>Ctrl+Shift+C</c>, Lowercase <c>Ctrl+Shift+K</c> (the letter L cannot be used —
/// <c>Ctrl+Shift+L</c> is taken by Bulleted List). This is guarded by the test
/// <c>AppActionCatalogTests.No_default_shortcut_blocks_a_polish_AltGr_character</c>.
/// The remaining <c>Ctrl+Alt+&lt;letter&gt;</c> shortcuts (V, U, M, T, B, W, F, J, Q) do not clash
/// with the Polish layout but may clash with other layouts (e.g. German AltGr+Q = @,
/// AltGr+M = µ) — a full solution would require AltGr detection, which is deliberately not done.
/// </para>
/// </summary>
public static class AppActionCatalog
{
    /// <summary>All action descriptors, in roughly menu order.</summary>
    public static IReadOnlyList<AppActionDescriptor> All { get; } = BuildAll();

    private static List<AppActionDescriptor> BuildAll()
    {
        List<AppActionDescriptor> all = new()
        {
        new("MainWindow.NewDefault", "New Default", "Ctrl+N", "document-new", "File"),
        new("MainWindow.NewEpub2", "ePub&2", "", "document-new-epub2", "File"),
        new("MainWindow.NewEpub3", "ePub&3", "", "document-new-epub3", "File"),
        new("MainWindow.NewHTMLFile", "Blank HTML File", "", null, "File"),
        new("MainWindow.NewCSSFile", "Blank Stylesheet", "", null, "File"),
        new("MainWindow.NewJSFile", "Blank Javascript", "", null, "File"),
        new("MainWindow.NewSVGFile", "Blank SVG Image", "", null, "File"),
        new("MainWindow.AddExistingFile", "Existing Files...", "", "document-add", "File"),
        new("MainWindow.Open", "&Open...", "Ctrl+O", "document-open", "File"),
        new("MainWindow.Close", "Close", "Ctrl+Shift+W", null, "File"),
        new("MainWindow.Save", "&Save", "Ctrl+S", "document-save", "File"),
        new("MainWindow.SaveAs", "Save &As...", "Ctrl+Shift+S", null, "File"),
        new("MainWindow.SaveACopy", "Save A &Copy...", "", null, "File"),
        new("MainWindow.PrintPreview", "Print Pre&view...", "", "document-print-preview", "File"),
        new("MainWindow.Print", "&Print...", "Ctrl+P", "document-print", "File"),
        new("MainWindow.Exit", "&Quit", "Ctrl+Q", "process-stop", "File"),

        new("MainWindow.Undo", "&Undo", "Ctrl+Z", "edit-undo", "Edit"),
        new("MainWindow.Redo", "&Redo", "Ctrl+Y", "edit-redo", "Edit"),
        // Book checkpoints — no default shortcuts
        // (Ctrl+Left/Right would clash with word navigation in Code View).
        new("MainWindow.RevertToBefore", "Revert to &Before", "", null, "Edit"),
        new("MainWindow.RevertToAfter", "Revert to &After", "", null, "Edit"),
        new("MainWindow.CreateCheckpoint", "Create &Checkpoint...", "", null, "Edit"),
        new("MainWindow.Cut", "Cu&t", "", "edit-cut", "Edit"),
        new("MainWindow.Copy", "&Copy", "", "edit-copy", "Edit"),
        new("MainWindow.Paste", "&Paste", "", "edit-paste", "Edit"),
        new("MainWindow.PasteClipboardHistory", "Edit/Paste From Clipboard &History...", "Ctrl+Alt+V", null, "Edit"),
        new("MainWindow.DeleteLine", "&Delete Line", "Ctrl+D", null, "Edit"),
        new("MainWindow.ToggleComment", "Toggle &Comment", "Ctrl+Shift+/", null, "Edit"),
        new("MainWindow.CasingLowercase", "&Lowercase", "Ctrl+Shift+K", "format-case-lowercase", "Edit"),
        new("MainWindow.CasingUppercase", "&Uppercase", "Ctrl+Alt+U", "format-case-uppercase", "Edit"),
        new("MainWindow.CasingTitlecase", "&Titlecase", "", "format-case-titlecase", "Edit"),
        new("MainWindow.CasingCapitalize", "&Capitalize", "", "format-case-capitalize", "Edit"),
        new("MainWindow.SplitSection", "&Split File At Cursor", "Ctrl+Return", "split-section", "Edit"),
        new("MainWindow.SplitOnSGFSectionMarkers", "Split At &Markers", "F6", null, "Edit"),
        new("MainWindow.Preferences", "&Preferences...", "F5", null, "Edit"),
        new("MainWindow.MergeContent", "Merge C&ontent", "", null, "Edit"),
        new("MainWindow.RenameClass", "Rename C&lass...", "", null, "Edit"),

        new("MainWindow.InsertSGFSectionMarker", "Split &Marker", "Ctrl+Shift+Return", null, "Insert"),
        new("MainWindow.InsertFile", "&File...", "Ctrl+Shift+I", "insert-image", "Insert"),
        new("MainWindow.InsertSpecialCharacter", "&Special Character...", "", "insert-special-character", "Insert"),
        new("MainWindow.InsertId", "I&D...", "", "insert-id", "Insert"),
        new("MainWindow.InsertClip", "Aria Clip...", "", "insert-aria-clip", "Insert"),
        new("MainWindow.InsertRole", "Role...", "", "insert-aria-role", "Insert"),
        new("MainWindow.InsertHyperlink", "&Link...", "", "insert-hyperlink", "Insert"),
        new("MainWindow.InsertClosingTag", "&Closing Tag", "Ctrl+.", null, "Insert"),

        new("MainWindow.Bold", "&Bold", "Ctrl+B", "format-text-bold", "Format"),
        new("MainWindow.Italic", "&Italic", "Ctrl+I", "format-text-italic", "Format"),
        new("MainWindow.Underline", "&Underline", "Ctrl+U", "format-text-underline", "Format"),
        new("MainWindow.Strikethrough", "Stri&kethrough", "", "format-text-strikethrough", "Format"),
        new("MainWindow.Subscript", "&Subscript", "", "format-text-subscript", "Format"),
        new("MainWindow.Superscript", "Su&perscript", "", "format-text-superscript", "Format"),
        new("MainWindow.AlignLeft", "Align &Left", "", "format-justify-left", "Format"),
        new("MainWindow.AlignCenter", "&Center", "Ctrl+E", "format-justify-center", "Format"),
        new("MainWindow.AlignRight", "Align &Right", "", "format-justify-right", "Format"),
        new("MainWindow.AlignJustify", "&Justify", "Ctrl+J", "format-justify-fill", "Format"),
        new("MainWindow.InsertNumberedList", "&Numbered List", "", "insert-numbered-list", "Format"),
        new("MainWindow.InsertBulletedList", "Bulle&ted List", "Ctrl+Shift+L", "insert-bullet-list", "Format"),
        new("MainWindow.IncreaseIndent", "Incre&ase Indent", "Ctrl+Alt+M", "format-indent-more", "Format"),
        new("MainWindow.DecreaseIndent", "&Decrease Indent", "Ctrl+Shift+M", "format-indent-less", "Format"),
        new("MainWindow.TextDirectionLTR", "Te&xt Direction LTR", "", "format-direction-ltr", "Format"),
        new("MainWindow.TextDirectionRTL", "T&ext Direction RTL", "", "format-direction-rtl", "Format"),
        new("MainWindow.TextDirectionDefault", "Text Directi&on Default", "", "format-direction-default", "Format"),
        new("MainWindow.RemoveFormatting", "Remove &Formatting", "Ctrl+Space", null, "Format"),
        new("MainWindow.RemoveTagPair", "Remove Tag Pair", "", "tag-pair-remove", "Format"),
        new("MainWindow.JumpToOpeningTag", "Jump To &Opening Tag", "Ctrl+Shift+[", null, "Format"),
        new("MainWindow.JumpToClosingTag", "Jump To C&losing Tag", "Ctrl+Shift+]", null, "Format"),
        new("MainWindow.SelectTagContents", "Select Tag &Contents", "Ctrl+Alt+T", null, "Format"),
        new("MainWindow.RenameTag", "Re&name Tag...", "", null, "Format"),
        new("MainWindow.SplitTag", "S&plit Tag", "", null, "Format"),
        new("MainWindow.Heading1", "Heading &1", "Ctrl+1", "heading-1", "Format"),
        new("MainWindow.Heading2", "Heading &2", "Ctrl+2", "heading-2", "Format"),
        new("MainWindow.Heading3", "Heading &3", "Ctrl+3", "heading-3", "Format"),
        new("MainWindow.Heading4", "Heading &4", "Ctrl+4", "heading-4", "Format"),
        new("MainWindow.Heading5", "Heading &5", "Ctrl+5", "heading-5", "Format"),
        new("MainWindow.Heading6", "Heading &6", "Ctrl+6", "heading-6", "Format"),
        new("MainWindow.HeadingNormal", "&Normal", "Ctrl+7", "heading-normal", "Format"),
        new("MainWindow.HeadingPreserveAttributes", "&Preserve Existing Attributes", "", null, "Format"),

        new("MainWindow.Find", "&Find && Replace...", "Ctrl+F", "edit-find", "Search"),
        new("MainWindow.HideFind", "&Hide Find && Replace...", "", null, "Search"),
        new("MainWindow.FindNext", "Find &Next", "Ctrl+G", null, "Search"),
        new("MainWindow.FindPrevious", "Find &Previous", "Ctrl+Shift+G", null, "Search"),
        new("MainWindow.ReplaceCurrent", "Replace", "Ctrl+R", null, "Search"),
        new("MainWindow.ReplaceNext", "&Replace/Find Next", "Ctrl+]", null, "Search"),
        new("MainWindow.ReplacePrevious", "R&eplace/Find Previous", "Ctrl+[", null, "Search"),
        new("MainWindow.ReplaceAll", "Replace &All", "Ctrl+Shift+A", null, "Search"),
        new("MainWindow.Count", "&Count All", "Ctrl+Shift+N", null, "Search"),
        new("MainWindow.DryRunReplaceAll", "Dry Run Replace All", "", null, "Search"),
        new("MainWindow.FilterReplaceAll", "Filter Replacements", "", null, "Search"),
        new("MainWindow.RestartSearch", "Restart Current Search", "", null, "Search"),
        new("MainWindow.FindNextInFile", "Find &Next In File", "", null, "Search"),
        new("MainWindow.ReplaceNextInFile", "&Replace Next In File", "", null, "Search"),
        new("MainWindow.ReplaceAllInFile", "Replace &All In File", "", null, "Search"),
        new("MainWindow.CountInFile", "&Count All In File", "", null, "Search"),
        new("MainWindow.BookmarkLocation", "Book&mark Location", "Ctrl+Alt+B", "bookmark", "Search"),
        new("MainWindow.GoToLinkOrStyle", "&Go To Link Or Style", "F3", null, "Search"),
        // Navigate Back / Forward (IntelliJ-style navigation history) — no default shortcut, set in Preferences.
        new("MainWindow.NavigateBack", "Navigate &Back", "", null, "Search"),
        new("MainWindow.NavigateForward", "Navigate For&ward", "", null, "Search"),
        new("MainWindow.RecentLocations", "Recent &Locations...", "", null, "Search"),
        new("MainWindow.FindUsages", "Find Usages", "Alt+F7", null, "Search"),
        new("MainWindow.MarkSelection", "Mar&k Selected Text", "Ctrl+Shift+M", null, "Search"),
        new("MainWindow.GoToLine", "Go To &Line...", "Ctrl+/", null, "Search"),

        new("MainWindow.ZoomIn", "Zoom &In", "Ctrl+=", "list-add", "View"),
        new("MainWindow.ZoomOut", "Zoom &Out", "Ctrl+-", "list-remove", "View"),
        new("MainWindow.ZoomReset", "&Zoom Reset", "Ctrl+0", null, "View"),
        new("MainWindow.WordWrap", "&Word Wrap", "", null, "View"),

        new("MainWindow.NextTab", "&Next Tab", "Ctrl+PgDown", null, "Window"),
        new("MainWindow.PreviousTab", "&Previous Tab", "Ctrl+PgUp", null, "Window"),
        new("MainWindow.CloseTab", "&Close Tab", "Ctrl+W", null, "Window"),
        new("MainWindow.CloseOtherTabs", "Close &Other Tabs", "Ctrl+Alt+W", null, "Window"),
        new("MainWindow.PreviousResource", "Pre&vious File", "Alt+PgUp", null, "Window"),
        new("MainWindow.NextResource", "Next &File", "Alt+PgDown", null, "Window"),

        new("MainWindow.AddCover", "Add &Cover...", "", null, "Tools"),
        new("MainWindow.MetaEditor", "&Metadata Editor...", "F8", "metadata-editor", "Tools"),
        new("MainWindow.GenerateTOC", "&Generate Table Of Contents...", "Ctrl+T", "generate-toc", "Tools"),
        new("MainWindow.EditTOC", "&Edit Table Of Contents...", "", "edit-toc", "Tools"),
        new("MainWindow.CreateHTMLTOC", "&Create HTML from Table Of Contents", "", null, "Tools"),
        new("MainWindow.Standardize", "Standardize EPUB...", "", "polish", "Tools"),
        new("MainWindow.StandardizeEpub", "Restructure Epub to Signet Norm", "", null, "Tools"),
        new("MainWindow.UseStandardFileExtensions", "Use Standard File Extensions", "", null, "Tools"),
        new("MainWindow.RebaseManifestIDs", "Rebase OPF Manifest IDs on Current Filenames", "", null, "Tools"),
        new("MainWindow.CustomLayout", "Create a Custom Empty Epub", "", null, "Tools"),
        new("MainWindow.UpdateManifestMediaTypes", "Update OPF Manifest Media Types", "", null, "Tools"),
        new("MainWindow.UpdateManifestProperties", "&Update Manifest Properties", "", null, "Tools"),
        new("MainWindow.NCXGuideFromNav", "Generate &NCX/Guide for epub2 e-readers", "", null, "Tools"),
        new("MainWindow.RemoveNCXGuide", "Remove the NCX and Guide", "", null, "Tools"),
        new("MainWindow.RemoveNavFromGuide", "Remove Nav from Reading Order", "", null, "Tools"),
        new("MainWindow.AddNavToSpine", "Add Nav to Reading Order", "", null, "Tools"),
        new("MainWindow.AddNavToSpineNonLinear", "Add Nav to Reading Order with linear=\"no\"", "", null, "Tools"),
        new("MainWindow.MendPrettifyHTML", "&Prettify All HTML Files", "", "beautify", "Tools"),
        new("MainWindow.MendHTML", "&Mend All HTML Files", "", "html-fix", "Tools"),
        // See the comment on AppActionIds.PrettifyCurrentHtml.
        new("MainWindow.PrettifyCurrentHTML", "Prettify Code", "", "beautify", "Tools"),
        new("MainWindow.MendCurrentHTML", "Mend Code", "", "html-fix", "Tools"),
        new("MainWindow.WellFormedCheckEpub", "Well-Formed Check &EPUB", "F7", null, "Tools"),
        new("MainWindow.ValidateStylesheetsWithW3C", "Validate Stylesheets With &W3C", "", null, "Tools"),
        new("MainWindow.Reports", "&Reports...", "Ctrl+Shift+R", null, "Tools"),
        new("MainWindow.ClipEditor", "&Clip Editor...", "Ctrl+Shift+C", null, "Tools"),
        // See the comment on AppActionIds.SelectClip.
        new("MainWindow.SelectClip", "&Insert Clip From Library...", "", null, "Tools"),
        new("MainWindow.SearchEditor", "&Saved Searches...", "Ctrl+Alt+F", "saved-search", "Tools"),
        new("MainWindow.Cleanup", "&Cleanup...", "", "polish", "Tools"),
        new("MainWindow.LiveCssPanel", "&Live CSS Panel", "Ctrl+Alt+J", null, "Tools"),
        new("MainWindow.AddSoftHyphens", "Add &Soft Hyphens", "", null, "Tools"),
        new("MainWindow.RemoveSoftHyphens", "&Remove Soft Hyphens", "", null, "Tools"),
        new("MainWindow.SpellcheckEditor", "&Spellcheck...", "Ctrl+Alt+Q", "document-spellcheck", "Tools"),
        new("MainWindow.AutoSpellCheck", "&Highlight Misspelled Words", "", null, "Tools"),
        new("MainWindow.Spellcheck", "&Next Misspelled Word", "F4", null, "Tools"),
        new("MainWindow.AddMispelledWord", "&Add Misspelled Word", "", null, "Tools"),
        new("MainWindow.IgnoreMispelledWord", "&Ignore Misspelled Word", "", null, "Tools"),
        new("MainWindow.ClearIgnoredWords", "&Clear Ignored Words", "", null, "Tools"),

        new("MainWindow.About", "&About...", "", "help-browser", "Help"),

        // Panels.
        new("MainWindow.BookBrowser", "&Book Browser", "Alt+F1", null, "View"),
        new("MainWindow.ClipsWindow", "&Clips", "", null, "View"),
        new("MainWindow.PreviewWindow", "&Preview", "F10", null, "View"),
        new("MainWindow.TableOfContents", "&Table Of Contents", "Alt+F3", null, "View"),
        new("MainWindow.ValidationResults", "&Validation Results", "Alt+F2", null, "View"),
        new("MainWindow.CheckpointsWindow", "C&heckpoints", "", null, "View"),
        new("MainWindow.FindReplaceWindow", "&Find && Replace Panel", "", null, "View"),
        new("MainWindow.NotificationsWindow", "&Notifications Panel", "", null, "View"),
        new("MainWindow.FindUsagesWindow", "Find Usages Panel", "", null, "View"),
        new("MainWindow.FocusOnBookBrowser", "Focus on Book Browser", "", null, "View"),
        new("MainWindow.FocusOnCodeView", "Focus on Code View", "", null, "View"),
        new("MainWindow.FocusOnPreview", "Focus on Preview", "", null, "View"),
        new("MainWindow.FocusOnTOC", "Focus on Table Of Contents", "", null, "View"),
        new("MainWindow.FocusOnClips", "Focus on Clips", "", null, "View"),

        // Book Browser panel context menu, exposed here so that every item can be bound to a shortcut.
        // These shortcuts work ONLY when the focus is in the panel — BookBrowserView registers them
        // as its own KeyBindings (widget-scoped shortcuts), unlike the other actions, which are
        // window KeyBindings.
        // Default shortcuts: Rename = F2 (Windows convention), Merge = Ctrl+M, Delete = Del.
        new(AppActionIds.BookBrowserOpen, "&Open", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserRename, "&Rename...", "F2", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserRenameWithTemplate, "Rename With &Template...", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserRegexRename, "Regex Rename...", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserDelete, "&Delete", "Del", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMove, "&Move To Folder...", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMoveUp, "Move &Up", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMoveDown, "Move Do&wn", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMoveToTop, "Move To &Top", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMoveToBottom, "Move To &Bottom", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserSort, "&Sort", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMerge, "Mer&ge", "Ctrl+M", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserSplit, "Split At Markers", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddBlankHtml, "Add Blank HTML File", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddBlankCss, "Add Blank Stylesheet", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddBlankSvg, "Add Blank SVG Image", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddBlankJs, "Add Blank Javascript", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddExistingFiles, "Add Existing Files...", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddSemantics, "Add Semantics...", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserCoverImage, "Cover Image", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMarkAsNav, "Mark As Table Of Contents (NAV)", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserLinkStylesheets, "Link Stylesheets...", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserLinkJavascripts, "Link Javascripts...", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserValidateWithW3C, "Validate With W3C", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddCopy, "Add Copy", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserRenumberToc, "Renumber TOC Entries", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserSaveAs, "Save As...", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserSelectAll, "Select All In Folder", "", null, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserGetInfo, "Get Info", "", null, AppActionIds.BookBrowserCategory),

        // Clip Editor window / Clips panel: panel-scoped like the Book Browser actions above.
        new(AppActionIds.ClipEditorRename, "&Rename", "F2", null, AppActionIds.ClipEditorCategory),
        };

        // Clip Bar / Cli&p / Cli&p2 menu slots. The default text is "Clip N" — the slot name stays
        // visible until a clip is assigned (an empty text would produce 60 indistinguishable rows
        // in Preferences → Keyboard Shortcuts).
        // MainWindowViewModel.RefreshClipActions() replaces it with the name of the assigned clip
        // and restores this value when the slot is empty.
        // Default shortcuts: only Clip1..10 (Ctrl+Alt+1..9, Ctrl+Alt+0)
        // and Clip60 (Ctrl+0 — intentionally shares its shortcut with MainWindow.ZoomReset).
        for (int slot = 1; slot <= AppActionIds.ClipSlotCount; slot++)
        {
            string shortcut = slot switch
            {
                >= 1 and <= 9 => "Ctrl+Alt+" + slot.ToString(CultureInfo.InvariantCulture),
                10 => "Ctrl+Alt+0",
                60 => "Ctrl+0",
                _ => string.Empty,
            };

            all.Add(new AppActionDescriptor(
                AppActionIds.Clip(slot),
                "Clip " + slot.ToString(CultureInfo.InvariantCulture),
                shortcut,
                null,
                "Clip"));
        }

        return all;
    }
}
