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
        new("MainWindow.NewHTMLFile", "Blank HTML File", "", "file-new-html", "File"),
        new("MainWindow.NewCSSFile", "Blank Stylesheet", "", "file-new-css", "File"),
        new("MainWindow.NewJSFile", "Blank Javascript", "", "file-new-js", "File"),
        new("MainWindow.NewSVGFile", "Blank SVG Image", "", "file-new-svg", "File"),
        new("MainWindow.AddExistingFile", "Existing Files...", "", "document-add", "File"),
        new("MainWindow.Open", "&Open...", "Ctrl+O", ActionIconKeys.Open, "File"),
        new("MainWindow.Close", "Close", "Ctrl+Shift+W", "document-close", "File"),
        new("MainWindow.Save", "&Save", "Ctrl+S", "document-save", "File"),
        new("MainWindow.SaveAs", "Save &As...", "Ctrl+Shift+S", "document-save-as", "File"),
        new("MainWindow.SaveACopy", "Save A &Copy...", "", "document-save-copy", "File"),
        new("MainWindow.PrintPreview", "Print Pre&view...", "", "document-print-preview", "File"),
        new("MainWindow.Print", "&Print...", "Ctrl+P", "document-print", "File"),
        new("MainWindow.Exit", "&Quit", "Ctrl+Q", "process-stop", "File"),

        new("MainWindow.Undo", "&Undo", "Ctrl+Z", "edit-undo", "Edit"),
        new("MainWindow.Redo", "&Redo", "Ctrl+Y", "edit-redo", "Edit"),
        // Book checkpoints — no default shortcuts
        // (Ctrl+Left/Right would clash with word navigation in Code Editor).
        new("MainWindow.RevertToBefore", "Revert to &Before", "", "revert-before", "Edit"),
        new("MainWindow.RevertToAfter", "Revert to &After", "", "revert-after", "Edit"),
        new("MainWindow.CreateCheckpoint", "Create &Checkpoint...", "", "git-commit", "Edit"),
        new("MainWindow.Cut", "Cu&t", "", "edit-cut", "Edit"),
        new("MainWindow.Copy", "&Copy", "", "edit-copy", "Edit"),
        new("MainWindow.Paste", "&Paste", "", "edit-paste", "Edit"),
        new("MainWindow.PasteClipboardHistory", "Edit/Paste From Clipboard &History...", "Ctrl+Alt+V", "paste-history", "Edit"),
        new("MainWindow.DeleteLine", "&Delete Line", "Ctrl+D", "delete-line", "Edit"),
        new("MainWindow.ToggleComment", "Toggle &Comment", "Ctrl+Shift+/", "toggle-comment", "Edit"),
        new("MainWindow.CasingLowercase", "&Lowercase", "Ctrl+Shift+K", "format-case-lowercase", "Edit"),
        new("MainWindow.CasingUppercase", "&Uppercase", "Ctrl+Alt+U", "format-case-uppercase", "Edit"),
        new("MainWindow.CasingTitlecase", "&Titlecase", "", "format-case-titlecase", "Edit"),
        new("MainWindow.CasingCapitalize", "&Capitalize", "", "format-case-capitalize", "Edit"),
        new("MainWindow.SplitSection", "&Split File At Cursor", "Ctrl+Return", "split-section", "Edit"),
        new("MainWindow.SplitOnSGFSectionMarkers", "Split At &Markers", "F6", "split-markers", "Edit"),
        new("MainWindow.Preferences", "&Preferences...", "F5", "settings", "Edit"),
        new("MainWindow.MergeContent", "Merge C&ontent", "", "merge-content", "Edit"),
        new("MainWindow.UnwrapDiv", "Unwrap <div>", "", "unwrap-div", "Edit"),
        new("MainWindow.RenameClass", "Rename C&lass...", "", "rename-class", "Edit"),

        new("MainWindow.InsertSGFSectionMarker", "Split &Marker", "Ctrl+Shift+Return", "section-marker", "Insert"),
        new("MainWindow.InsertFile", "&File...", "Ctrl+Shift+I", "insert-image", "Insert"),
        new("MainWindow.InsertSpecialCharacter", "&Special Character...", "", "insert-special-character", "Insert"),
        new("MainWindow.InsertId", "I&D...", "", "insert-id", "Insert"),
        new("MainWindow.InsertClip", "Aria Clip...", "", "insert-aria-clip", "Insert"),
        new("MainWindow.InsertRole", "Role...", "", "insert-aria-role", "Insert"),
        new("MainWindow.InsertHyperlink", "&Link...", "", "insert-hyperlink", "Insert"),
        new("MainWindow.InsertClosingTag", "&Closing Tag", "Ctrl+.", "tag-closing", "Insert"),

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
        new("MainWindow.RemoveFormatting", "Remove &Formatting", "Ctrl+Space", "remove-formatting", "Format"),
        new("MainWindow.RemoveTagPair", "Remove Tag Pair", "", "tag-pair-remove", "Format"),
        new("MainWindow.JumpToOpeningTag", "Jump To &Opening Tag", "Ctrl+Shift+[", "tag-jump-opening", "Format"),
        new("MainWindow.JumpToClosingTag", "Jump To C&losing Tag", "Ctrl+Shift+]", "tag-jump-closing", "Format"),
        new("MainWindow.SelectTagContents", "Select Tag &Contents", "Ctrl+Alt+T", "tag-select", "Format"),
        new("MainWindow.RenameTag", "Re&name Tag...", "", "tag-rename", "Format"),
        new("MainWindow.SplitTag", "S&plit Tag", "", "tag-split", "Format"),
        new("MainWindow.Heading1", "Heading &1", "Ctrl+1", "heading-1", "Format"),
        new("MainWindow.Heading2", "Heading &2", "Ctrl+2", "heading-2", "Format"),
        new("MainWindow.Heading3", "Heading &3", "Ctrl+3", "heading-3", "Format"),
        new("MainWindow.Heading4", "Heading &4", "Ctrl+4", "heading-4", "Format"),
        new("MainWindow.Heading5", "Heading &5", "Ctrl+5", "heading-5", "Format"),
        new("MainWindow.Heading6", "Heading &6", "Ctrl+6", "heading-6", "Format"),
        new("MainWindow.HeadingNormal", "&Normal", "Ctrl+7", "heading-normal", "Format"),
        new("MainWindow.HeadingPreserveAttributes", "&Preserve Existing Attributes", "", "preserve-attributes", "Format"),

        new("MainWindow.Find", "&Find && Replace...", "Ctrl+F", "edit-find", "Search"),
        // Intentionally absent from MenuLayout so the keymap lists these actions under "Other".
        new("MainWindow.SearchAllFilesOfSameType", "Search in All Files of Same Type", "", "find", "Search"),
        new("MainWindow.SearchAllTextFiles", "Search in All Text Files", "", "find", "Search"),
        new("MainWindow.HideFind", "&Hide Find && Replace...", "", "find-hide", "Search"),
        new("MainWindow.FindNext", "Find &Next", "Ctrl+G", "find-next", "Search"),
        new("MainWindow.FindPrevious", "Find &Previous", "Ctrl+Shift+G", "find-previous", "Search"),
        new("MainWindow.ReplaceCurrent", "Replace", "Ctrl+R", "replace", "Search"),
        new("MainWindow.ReplaceNext", "&Replace/Find Next", "Ctrl+]", "replace-find", "Search"),
        new("MainWindow.ReplacePrevious", "R&eplace/Find Previous", "Ctrl+[", "replace-find", "Search"),
        new("MainWindow.ReplaceAll", "Replace &All", "Ctrl+Shift+A", "replace-all", "Search"),
        new("MainWindow.Count", "&Count All", "Ctrl+Shift+N", "count-all", "Search"),
        new("MainWindow.DryRunReplaceAll", "Dry Run Replace All", "", "replace-all-dry-run", "Search"),
        new("MainWindow.FilterReplaceAll", "Filter Replacements", "", "replace-all-filter", "Search"),
        new("MainWindow.RestartSearch", "Restart Current Search", "", "restart", "Search"),
        new("MainWindow.FindNextInFile", "Find &Next In File", "", "find-next", "Search"),
        new("MainWindow.ReplaceNextInFile", "&Replace Next In File", "", "replace-find", "Search"),
        new("MainWindow.ReplaceAllInFile", "Replace &All In File", "", "replace-all", "Search"),
        new("MainWindow.CountInFile", "&Count All In File", "", "count-all", "Search"),
        new("MainWindow.BookmarkLocation", "Book&mark Location", "Ctrl+Alt+B", "bookmark", "Search"),
        new("MainWindow.GoToLinkOrStyle", "&Go To Link Or Style", "F3", "go-to-definition", "Search"),
        // Navigate Back / Forward (IntelliJ-style navigation history) — no default shortcut, set in Preferences.
        new("MainWindow.NavigateBack", "Navigate &Back", "", "back", "Search"),
        new("MainWindow.NavigateForward", "Navigate For&ward", "", "forward", "Search"),
        new("MainWindow.RecentLocations", "Recent &Locations...", "", "recent-locations", "Search"),
        new("MainWindow.FindUsages", "Find Usages", "Alt+F7", "find-usages", "Search"),
        new("MainWindow.MarkSelection", "Mar&k Selected Text", "Ctrl+Shift+M", "mark-selection", "Search"),
        new("MainWindow.GoToLine", "Go To &Line...", "Ctrl+/", "go-to-line", "Search"),

        new("MainWindow.ZoomIn", "Zoom &In", "Ctrl+=", "list-add", "View"),
        new("MainWindow.ZoomOut", "Zoom &Out", "Ctrl+-", "list-remove", "View"),
        new("MainWindow.ZoomReset", "&Zoom Reset", "Ctrl+0", "zoom-to-window", "View"),
        new("MainWindow.WordWrap", "&Word Wrap", "", "word-wrap", "View"),

        new("MainWindow.NextTab", "&Next Tab", "Ctrl+PgDown", "tab-next", "Window"),
        new("MainWindow.PreviousTab", "&Previous Tab", "Ctrl+PgUp", "tab-previous", "Window"),
        new("MainWindow.CloseTab", "&Close Tab", "Ctrl+W", "tab-close", "Window"),
        new("MainWindow.CloseOtherTabs", "Close &Other Tabs", "Ctrl+Alt+W", "tab-close-others", "Window"),
        new("MainWindow.PreviousResource", "Pre&vious File", "Alt+PgUp", "file-previous", "Window"),
        new("MainWindow.NextResource", "Next &File", "Alt+PgDown", "file-next", "Window"),

        new("MainWindow.AddCover", "Add &Cover...", "", "add-cover", "Tools"),
        new("MainWindow.MetaEditor", "&Metadata Editor...", "F8", "metadata-editor", "Tools"),
        new("MainWindow.GenerateTOC", "&Generate Table Of Contents...", "Ctrl+T", "generate-toc", "Tools"),
        new("MainWindow.EditTOC", "&Edit Table Of Contents...", "", "edit-toc", "Tools"),
        new("MainWindow.CreateHTMLTOC", "&Create HTML from Table Of Contents", "", "generate-toc-html", "Tools"),
        new("MainWindow.Standardize", "Standardize EPUB...", "", "polish", "Tools"),
        new("MainWindow.StandardizeEpub", "Restructure Epub to Signet Norm", "", "standardize", "Tools"),
        new("MainWindow.UseStandardFileExtensions", "Use Standard File Extensions", "", "standard-extensions", "Tools"),
        new("MainWindow.RebaseManifestIDs", "Rebase OPF Manifest IDs on Current Filenames", "", "rebase-ids", "Tools"),
        new("MainWindow.CustomLayout", "Create a Custom Empty Epub", "", "custom-epub", "Tools"),
        new("MainWindow.UpdateManifestMediaTypes", "Update OPF Manifest Media Types", "", "manifest-media-types", "Tools"),
        new("MainWindow.UpdateManifestProperties", "&Update Manifest Properties", "", "manifest-properties", "Tools"),
        new("MainWindow.UpgradeToEpub3", "Upgrade to EPUB &3", "", "upgrade-epub3", "Tools"),
        new("MainWindow.NCXGuideFromNav", "Generate &NCX/Guide for epub2 e-readers", "", "ncx-generate", "Tools"),
        new("MainWindow.RemoveNCXGuide", "Remove the NCX and Guide", "", "ncx-remove", "Tools"),
        new("MainWindow.RemoveNavFromGuide", "Remove Nav from Reading Order", "", "nav-remove", "Tools"),
        new("MainWindow.AddNavToSpine", "Add Nav to Reading Order", "", "nav-add", "Tools"),
        new("MainWindow.AddNavToSpineNonLinear", "Add Nav to Reading Order with linear=\"no\"", "", "nav-add-nonlinear", "Tools"),
        new("MainWindow.MendPrettifyHTML", "&Prettify All HTML Files", "", "beautify-html", "Tools"),
        new("MainWindow.MendHTML", "&Mend All HTML Files", "", "html-fix", "Tools"),
        // See the comment on AppActionIds.PrettifyCurrentHtml.
        new("MainWindow.PrettifyCurrentHTML", "Prettify Code", "", "beautify-html", "Tools"),
        new("MainWindow.MendCurrentHTML", "Mend Code", "", "html-fix", "Tools"),
        // Reformat CSS — the current stylesheet and every stylesheet of the book (its own "beautify-css" icon, the
        // tulip of its HTML counterpart with a "CSS" label instead of "HTML": the two are never enabled at the same
        // time, one wants an HTML tab and the other a CSS tab).
        new("MainWindow.PrettifyCurrentCSS", "Prettify &Stylesheet", "", "beautify-css", "Tools"),
        new("MainWindow.PrettifyAllCSS", "Prettify &All Stylesheets", "", "beautify-css", "Tools"),
        new("MainWindow.WellFormedCheckEpub", "Well-Formed Check &EPUB", "F7", "check-wellformed", "Tools"),
        new("MainWindow.ValidateStylesheetsWithW3C", "Validate Stylesheets With &W3C", "", "validate-css", "Tools"),
        new("MainWindow.Reports", "&Reports...", "Ctrl+Shift+R", "reports", "Tools"),
        new("MainWindow.ClipEditor", "&Clip Editor...", "Ctrl+Shift+C", "clip-editor", "Tools"),
        // See the comment on AppActionIds.SelectClip.
        new("MainWindow.SelectClip", "&Insert Clip From Library...", "", "insert-clip", "Tools"),
        new("MainWindow.SearchEditor", "&Saved Searches...", "Ctrl+Alt+F", "saved-search", "Tools"),
        new("MainWindow.Cleanup", "&Cleanup...", "", "polish", "Tools"),
        new("MainWindow.ManageFonts", "Manage &Fonts...", "", "manage-fonts", "Tools"),
        new("MainWindow.LiveCssPanel", "&Live CSS Panel", "Ctrl+Alt+J", "inspect", "Tools"),
        new("MainWindow.RenumberFootnotes", "&Renumber Footnotes", "", "footnotes-renumber", "Tools"),
        new("MainWindow.ConvertFootnotes", "&Convert Footnotes of Other Origin...", "", "footnotes-convert", "Tools"),
        new("MainWindow.UnifyFootnoteLetters", "&Unify Letters of Shared Footnotes...", "", "footnotes-unify-letters", "Tools"),
        new("MainWindow.ChangeFootnotePlacement", "Change Footnote &Placement (Local / Global)...", "", "footnotes-convert", "Tools"),
        new("MainWindow.AddSoftHyphens", "Add &Soft Hyphens", "", "hyphen-add", "Tools"),
        new("MainWindow.RemoveSoftHyphens", "&Remove Soft Hyphens", "", "hyphen-remove", "Tools"),
        new("MainWindow.SpellcheckEditor", "&Spellcheck...", "Ctrl+Alt+Q", "document-spellcheck", "Tools"),
        new("MainWindow.AutoSpellCheck", "&Highlight Misspelled Words", "", "spellcheck-highlight", "Tools"),
        new("MainWindow.Spellcheck", "&Next Misspelled Word", "F4", "spellcheck-next", "Tools"),
        new("MainWindow.AddMispelledWord", "&Add Misspelled Word", "", "spellcheck-add", "Tools"),
        new("MainWindow.IgnoreMispelledWord", "&Ignore Misspelled Word", "", "spellcheck-ignore", "Tools"),
        new("MainWindow.ClearIgnoredWords", "&Clear Ignored Words", "", "spellcheck-clear-ignored", "Tools"),

        new("MainWindow.CssPropertyReference", "&CSS Properties in EPUB...", "", "help-browser", "Help"),
        new("MainWindow.About", "&About...", "", "help-browser", "Help"),

        // Panels.
        new("MainWindow.BookBrowser", "&Book Browser", "Alt+F1", "panel-book-browser", "View"),
        new("MainWindow.ClipsWindow", "&Clips", "", "panel-clips", "View"),
        new("MainWindow.PreviewWindow", "&Preview", "F10", "panel-preview", "View"),
        new("MainWindow.TableOfContents", "&Table Of Contents", "Alt+F3", "edit-toc", "View"),
        new("MainWindow.ValidationResults", "&Validation Results", "Alt+F2", "panel-validation", "View"),
        new("MainWindow.CheckpointsWindow", "C&heckpoints", "", "git-manage", "View"),
        new("MainWindow.FindReplaceWindow", "&Find && Replace Panel", "", "find", "View"),
        new("MainWindow.NotificationsWindow", "&Notifications Panel", "", "panel-notifications", "View"),
        new("MainWindow.FindUsagesWindow", "Find Usages Panel", "", "find-usages", "View"),
        new("MainWindow.FocusOnBookBrowser", "Focus on Book Browser", "", "focus-book-browser", "View"),
        new("MainWindow.FocusOnCodeEditor", "Focus on Code Editor", "", "focus-code-editor", "View"),
        new("MainWindow.FocusOnPreview", "Focus on Preview", "", "focus-preview", "View"),
        new("MainWindow.FocusOnTOC", "Focus on Table Of Contents", "", "focus-toc", "View"),
        new("MainWindow.FocusOnClips", "Focus on Clips", "", "focus-clips", "View"),

        // Book Browser panel context menu, exposed here so that every item can be bound to a shortcut.
        // These shortcuts work ONLY when the focus is in the panel — BookBrowserView registers them
        // as its own KeyBindings (widget-scoped shortcuts), unlike the other actions, which are
        // window KeyBindings.
        // Default shortcuts: Rename = F2 (Windows convention), Merge = Ctrl+M, Delete = Del.
        new(AppActionIds.BookBrowserOpen, "&Open", "", "document-open", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserRename, "&Rename...", "F2", "rename", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserRenameWithTemplate, "&Mass Rename...", "", "rename-template", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserRegexRename, "Regex Rename...", "", "rename-regex", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserDelete, "&Delete", "Del", "delete", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMove, "&Move To Folder...", "", "move-to-folder", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMoveUp, "Move &Up", "", "move-up", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMoveDown, "Move Do&wn", "", "move-down", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMoveToTop, "Move To &Top", "", "move-to-top", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMoveToBottom, "Move To &Bottom", "", "move-to-bottom", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserSort, "&Sort", "", "sort", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMerge, "Mer&ge", "Ctrl+M", "merge-content", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserSplit, "Split At Markers", "", "split-markers", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddBlankHtml, "Add Blank HTML File", "", "file-add-html", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddBlankCss, "Add Blank Stylesheet", "", "file-add-css", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddBlankSvg, "Add Blank SVG Image", "", "file-add-svg", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddBlankJs, "Add Blank Javascript", "", "file-add-js", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddExistingFiles, "Add Existing Files...", "", "document-add", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddFontFiles, "Add Font Files...", "", "font-add", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddSemantics, "Add Semantics...", "", "semantics-add", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserRemoveSemantics, "Remove Semantics", "", ActionIconKeys.Delete, AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserCoverImage, "Cover Image", "", "add-cover", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserMarkAsNav, "Mark As Table Of Contents (NAV)", "", "nav-mark", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserLinkStylesheets, "Link Stylesheets...", "", "link-css", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserLinkJavascripts, "Link Javascripts...", "", "link-js", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserValidateWithW3C, "Validate With W3C", "", "validate-w3c", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserAddCopy, "Add Copy", "", "file-copy", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserRenumberToc, "Renumber TOC Entries", "", "toc-renumber", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserSaveAs, "Save As...", "", "document-save-as", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserSelectAll, "Select All In Folder", "", "edit-select-all", AppActionIds.BookBrowserCategory),
        new(AppActionIds.BookBrowserGetInfo, "Get Info", "", "file-info", AppActionIds.BookBrowserCategory),

        // Clip Editor window / Clips panel: panel-scoped like the Book Browser actions above.
        new(AppActionIds.ClipEditorRename, "&Rename", "F2", "rename", AppActionIds.ClipEditorCategory),
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
