namespace Signet.App.Actions;

/// <summary>
/// Constant identifiers of the main window actions. The values are the keys under which
/// <c>KeyboardShortcutManager</c> stores shortcut overrides, so they must stay stable.
/// </summary>
public static class AppActionIds
{
    // File
    public const string NewDefault = "MainWindow.NewDefault";
    public const string NewEpub2 = "MainWindow.NewEpub2";
    public const string NewEpub3 = "MainWindow.NewEpub3";
    public const string NewHtmlFile = "MainWindow.NewHTMLFile";
    public const string NewCssFile = "MainWindow.NewCSSFile";
    public const string NewJsFile = "MainWindow.NewJSFile";
    public const string NewSvgFile = "MainWindow.NewSVGFile";
    public const string AddExistingFile = "MainWindow.AddExistingFile";
    public const string Open = "MainWindow.Open";
    public const string Save = "MainWindow.Save";
    public const string SaveAs = "MainWindow.SaveAs";
    public const string SaveACopy = "MainWindow.SaveACopy";
    public const string PrintPreview = "MainWindow.PrintPreview";
    public const string Print = "MainWindow.Print";
    public const string Close = "MainWindow.Close";
    public const string Exit = "MainWindow.Exit";

    // Edit
    public const string Undo = "MainWindow.Undo";
    public const string Redo = "MainWindow.Redo";
    public const string Cut = "MainWindow.Cut";
    public const string Copy = "MainWindow.Copy";
    public const string Paste = "MainWindow.Paste";
    public const string PasteClipboardHistory = "MainWindow.PasteClipboardHistory";
    public const string DeleteLine = "MainWindow.DeleteLine";
    public const string ToggleComment = "MainWindow.ToggleComment";
    public const string CasingLowercase = "MainWindow.CasingLowercase";
    public const string CasingUppercase = "MainWindow.CasingUppercase";
    public const string CasingTitlecase = "MainWindow.CasingTitlecase";
    public const string CasingCapitalize = "MainWindow.CasingCapitalize";
    public const string SplitSection = "MainWindow.SplitSection";
    public const string SplitOnSgfSectionMarkers = "MainWindow.SplitOnSGFSectionMarkers";
    public const string Preferences = "MainWindow.Preferences";

    // Insert
    public const string InsertSgfSectionMarker = "MainWindow.InsertSGFSectionMarker";
    public const string InsertFile = "MainWindow.InsertFile";
    public const string InsertSpecialCharacter = "MainWindow.InsertSpecialCharacter";
    public const string InsertId = "MainWindow.InsertId";
    public const string InsertClip = "MainWindow.InsertClip";
    public const string InsertRole = "MainWindow.InsertRole";
    public const string InsertHyperlink = "MainWindow.InsertHyperlink";
    public const string InsertClosingTag = "MainWindow.InsertClosingTag";

    // Format
    public const string Bold = "MainWindow.Bold";
    public const string Italic = "MainWindow.Italic";
    public const string Underline = "MainWindow.Underline";
    public const string Strikethrough = "MainWindow.Strikethrough";
    public const string Subscript = "MainWindow.Subscript";
    public const string Superscript = "MainWindow.Superscript";
    public const string AlignLeft = "MainWindow.AlignLeft";
    public const string AlignCenter = "MainWindow.AlignCenter";
    public const string AlignRight = "MainWindow.AlignRight";
    public const string AlignJustify = "MainWindow.AlignJustify";
    public const string InsertBulletedList = "MainWindow.InsertBulletedList";
    public const string InsertNumberedList = "MainWindow.InsertNumberedList";
    public const string IncreaseIndent = "MainWindow.IncreaseIndent";
    public const string DecreaseIndent = "MainWindow.DecreaseIndent";
    public const string TextDirectionLtr = "MainWindow.TextDirectionLTR";
    public const string TextDirectionRtl = "MainWindow.TextDirectionRTL";
    public const string TextDirectionDefault = "MainWindow.TextDirectionDefault";
    public const string RemoveFormatting = "MainWindow.RemoveFormatting";
    public const string RemoveTagPair = "MainWindow.RemoveTagPair";
    public const string JumpToOpeningTag = "MainWindow.JumpToOpeningTag";
    public const string JumpToClosingTag = "MainWindow.JumpToClosingTag";
    public const string SelectTagContents = "MainWindow.SelectTagContents";
    public const string RenameTag = "MainWindow.RenameTag";
    public const string SplitTag = "MainWindow.SplitTag";
    public const string MergeContent = "MainWindow.MergeContent";
    public const string RevertToBefore = "MainWindow.RevertToBefore";
    public const string RevertToAfter = "MainWindow.RevertToAfter";
    public const string CreateCheckpoint = "MainWindow.CreateCheckpoint";
    public const string Heading1 = "MainWindow.Heading1";
    public const string Heading2 = "MainWindow.Heading2";
    public const string Heading3 = "MainWindow.Heading3";
    public const string Heading4 = "MainWindow.Heading4";
    public const string Heading5 = "MainWindow.Heading5";
    public const string Heading6 = "MainWindow.Heading6";
    public const string HeadingNormal = "MainWindow.HeadingNormal";
    public const string HeadingPreserveAttributes = "MainWindow.HeadingPreserveAttributes";

    // Search
    public const string Find = "MainWindow.Find";
    public const string HideFind = "MainWindow.HideFind";
    public const string FindNext = "MainWindow.FindNext";
    public const string FindPrevious = "MainWindow.FindPrevious";
    public const string ReplaceCurrent = "MainWindow.ReplaceCurrent";
    public const string ReplaceNext = "MainWindow.ReplaceNext";
    public const string ReplacePrevious = "MainWindow.ReplacePrevious";
    public const string ReplaceAll = "MainWindow.ReplaceAll";
    public const string Count = "MainWindow.Count";
    public const string DryRunReplaceAll = "MainWindow.DryRunReplaceAll";
    public const string FilterReplaceAll = "MainWindow.FilterReplaceAll";
    public const string RestartSearch = "MainWindow.RestartSearch";
    public const string FindNextInFile = "MainWindow.FindNextInFile";
    public const string ReplaceNextInFile = "MainWindow.ReplaceNextInFile";
    public const string ReplaceAllInFile = "MainWindow.ReplaceAllInFile";
    public const string CountInFile = "MainWindow.CountInFile";
    public const string BookmarkLocation = "MainWindow.BookmarkLocation";
    public const string GoToLinkOrStyle = "MainWindow.GoToLinkOrStyle";
    public const string GoBackFromLinkOrStyle = "MainWindow.GoBackFromLinkOrStyle";
    public const string MarkSelection = "MainWindow.MarkSelection";
    public const string GoToLine = "MainWindow.GoToLine";

    // View
    public const string ZoomIn = "MainWindow.ZoomIn";
    public const string ZoomOut = "MainWindow.ZoomOut";
    public const string ZoomReset = "MainWindow.ZoomReset";

    /// <summary>Toggles word wrap in Code View.</summary>
    public const string WordWrap = "MainWindow.WordWrap";

    // Window
    public const string NextTab = "MainWindow.NextTab";
    public const string PreviousTab = "MainWindow.PreviousTab";
    public const string CloseTab = "MainWindow.CloseTab";
    public const string CloseOtherTabs = "MainWindow.CloseOtherTabs";
    public const string PreviousResource = "MainWindow.PreviousResource";
    public const string NextResource = "MainWindow.NextResource";

    // Tools
    public const string AddCover = "MainWindow.AddCover";
    public const string MetaEditor = "MainWindow.MetaEditor";
    public const string GenerateToc = "MainWindow.GenerateTOC";
    public const string EditToc = "MainWindow.EditTOC";
    public const string CreateHtmlToc = "MainWindow.CreateHTMLTOC";

    /// <summary>
    /// Standardize EPUB — one dialog with a preview for the four tidying steps below (standard folders, standard
    /// extensions, manifest IDs, manifest media types); those keep their own actions for keyboard shortcuts.
    /// </summary>
    public const string Standardize = "MainWindow.Standardize";
    public const string StandardizeEpub = "MainWindow.StandardizeEpub";
    public const string UseStandardFileExtensions = "MainWindow.UseStandardFileExtensions";
    public const string RebaseManifestIds = "MainWindow.RebaseManifestIDs";
    public const string UpdateManifestMediaTypes = "MainWindow.UpdateManifestMediaTypes";
    public const string UpdateManifestProperties = "MainWindow.UpdateManifestProperties";
    public const string CustomLayout = "MainWindow.CustomLayout";
    public const string NcxGuideFromNav = "MainWindow.NCXGuideFromNav";
    public const string RemoveNcxGuide = "MainWindow.RemoveNCXGuide";
    public const string RemoveNavFromSpine = "MainWindow.RemoveNavFromGuide";
    public const string AddNavToSpine = "MainWindow.AddNavToSpine";
    public const string AddNavToSpineNonLinear = "MainWindow.AddNavToSpineNonLinear";
    public const string MendPrettifyHtml = "MainWindow.MendPrettifyHTML";
    public const string MendHtml = "MainWindow.MendHTML";

    // "Beautify code" / "Fix code" for the current file — catalog actions (not only Code View
    // context menu items) so that they can be placed on a toolbar (icons: beautify / html-fix).
    public const string PrettifyCurrentHtml = "MainWindow.PrettifyCurrentHTML";
    public const string MendCurrentHtml = "MainWindow.MendCurrentHTML";
    public const string WellFormedCheckEpub = "MainWindow.WellFormedCheckEpub";
    public const string ValidateStylesheetsWithW3C = "MainWindow.ValidateStylesheetsWithW3C";
    public const string Reports = "MainWindow.Reports";
    public const string ClipEditor = "MainWindow.ClipEditor";

    /// <summary>
    /// "Insert Clip from Library..." — inserts a clip from the clip library via a menu item
    /// (in addition to the Clip Bar toolbar/panel/shortcuts). Not to be confused with
    /// <see cref="InsertClip"/>, which opens the "Aria Clip" dialog (<c>AddClips</c>) — an unrelated feature.
    /// </summary>
    public const string SelectClip = "MainWindow.SelectClip";

    public const string SearchEditor = "MainWindow.SearchEditor";

    /// <summary>Cleanup — removing unused stylesheets/selectors/media and merging CSS rules in one dialog.</summary>
    public const string Cleanup = "MainWindow.Cleanup";

    /// <summary>Find Usages — the usages of the CSS class under the caret, in the "Find Usages" panel.</summary>
    public const string FindUsages = "MainWindow.FindUsages";

    /// <summary>Shows/hides the "Find Usages" panel.</summary>
    public const string ToggleFindUsages = "MainWindow.FindUsagesWindow";

    /// <summary>Live CSS Panel.</summary>
    public const string LiveCssPanel = "MainWindow.LiveCssPanel";

    /// <summary>Add soft hyphens.</summary>
    public const string AddSoftHyphens = "MainWindow.AddSoftHyphens";

    /// <summary>Remove soft hyphens.</summary>
    public const string RemoveSoftHyphens = "MainWindow.RemoveSoftHyphens";
    public const string SpellcheckEditor = "MainWindow.SpellcheckEditor";
    public const string AutoSpellCheck = "MainWindow.AutoSpellCheck";
    public const string Spellcheck = "MainWindow.Spellcheck";
    public const string AddMisspelledWord = "MainWindow.AddMispelledWord";
    public const string IgnoreMisspelledWord = "MainWindow.IgnoreMispelledWord";
    public const string ClearIgnoredWords = "MainWindow.ClearIgnoredWords";

    // Help
    public const string About = "MainWindow.About";

    // Panel focus / visibility toggles
    public const string FocusCodeView = "MainWindow.FocusOnCodeView";
    public const string FocusBookBrowser = "MainWindow.FocusOnBookBrowser";
    public const string FocusPreview = "MainWindow.FocusOnPreview";
    public const string FocusToc = "MainWindow.FocusOnTOC";
    public const string FocusClips = "MainWindow.FocusOnClips";
    public const string ToggleBookBrowser = "MainWindow.BookBrowser";
    public const string ToggleClips = "MainWindow.ClipsWindow";
    public const string TogglePreview = "MainWindow.PreviewWindow";
    public const string ToggleToc = "MainWindow.TableOfContents";
    public const string ToggleValidationResults = "MainWindow.ValidationResults";
    public const string ToggleCheckpoints = "MainWindow.CheckpointsWindow";
    public const string ToggleNotifications = "MainWindow.NotificationsWindow";
    public const string ToggleFindReplace = "MainWindow.FindReplaceWindow";

    /// <summary>
    /// Category of the Book Browser panel context menu actions. Shortcuts of these actions work
    /// only when the focus is in the panel (<c>BookBrowserView</c> registers them as its own
    /// <c>KeyBinding</c>s, not the window's) — unlike all other actions.
    /// </summary>
    public const string BookBrowserCategory = "Book Browser";

    /// <summary>
    /// Category of the Clip Editor actions (the "Clip Editor" window and the docked "Clips" panel). Like
    /// <see cref="BookBrowserCategory"/>, their shortcuts work only while the focus is in the clips tree.
    /// </summary>
    public const string ClipEditorCategory = "Clip Editor";

    /// <summary>Renames the selected clip or group in the clips tree.</summary>
    public const string ClipEditorRename = "MainWindow.ClipEditor.Rename";

    /// <summary>Whether the category holds panel-scoped actions (handled only while the panel has focus).</summary>
    public static bool IsPanelCategory(string category) =>
        string.Equals(category, BookBrowserCategory, System.StringComparison.Ordinal)
        || string.Equals(category, ClipEditorCategory, System.StringComparison.Ordinal);

/// <summary>Opens the selected files.</summary>
    public const string BookBrowserOpen = "MainWindow.BookBrowser.Open";

    /// <summary>Renames the selected files.</summary>
    public const string BookBrowserRename = "MainWindow.BookBrowser.Rename";

    /// <summary>Renames using a template.</summary>
    public const string BookBrowserRenameWithTemplate = "MainWindow.BookBrowser.RenameWithTemplate";

    /// <summary>Renames using a regular expression (<c>RERename</c>).</summary>
    public const string BookBrowserRegexRename = "MainWindow.BookBrowser.RERename";

    /// <summary>Deletes the selected files.</summary>
    public const string BookBrowserDelete = "MainWindow.BookBrowser.Delete";

    /// <summary>Moves to another folder.</summary>
    public const string BookBrowserMove = "MainWindow.BookBrowser.Move";

    /// <summary>Moves up in the reading order.</summary>
    public const string BookBrowserMoveUp = "MainWindow.BookBrowser.MoveUp";

    /// <summary>Moves down in the reading order.</summary>
    public const string BookBrowserMoveDown = "MainWindow.BookBrowser.MoveDown";

    /// <summary>Sorts the selected files.</summary>
    public const string BookBrowserSort = "MainWindow.BookBrowser.Sort";

    /// <summary>Merges the selected files.</summary>
    public const string BookBrowserMerge = "MainWindow.BookBrowser.Merge";

    /// <summary>Splits the file at split markers.</summary>
    public const string BookBrowserSplit = "MainWindow.BookBrowser.Split";

    /// <summary>Adds a blank HTML file.</summary>
    public const string BookBrowserAddBlankHtml = "MainWindow.BookBrowser.AddBlankHtml";

    /// <summary>Adds a blank stylesheet.</summary>
    public const string BookBrowserAddBlankCss = "MainWindow.BookBrowser.AddBlankCss";

    /// <summary>Adds a blank SVG file.</summary>
    public const string BookBrowserAddBlankSvg = "MainWindow.BookBrowser.AddBlankSvg";

    /// <summary>Adds a blank JavaScript file.</summary>
    public const string BookBrowserAddBlankJs = "MainWindow.BookBrowser.AddBlankJs";

    /// <summary>Adds existing files from disk.</summary>
    public const string BookBrowserAddExistingFiles = "MainWindow.BookBrowser.AddExistingFiles";

    /// <summary>Adds semantics to the selected files.</summary>
    public const string BookBrowserAddSemantics = "MainWindow.BookBrowser.AddSemantics";

    /// <summary>Marks the image as the cover.</summary>
    public const string BookBrowserCoverImage = "MainWindow.BookBrowser.CoverImage";

    /// <summary>Links stylesheets.</summary>
    public const string BookBrowserLinkStylesheets = "MainWindow.BookBrowser.LinkStylesheets";

    /// <summary>Links JavaScript files.</summary>
    public const string BookBrowserLinkJavascripts = "MainWindow.BookBrowser.LinkJavascripts";

    /// <summary>Validates the selected CSS with the W3C validator.</summary>
    public const string BookBrowserValidateWithW3C = "MainWindow.BookBrowser.ValidateWithW3C";

    /// <summary>Adds a copy of the selected file.</summary>
    public const string BookBrowserAddCopy = "MainWindow.BookBrowser.AddCopy";

    /// <summary>Renumbers the table of contents entries.</summary>
    public const string BookBrowserRenumberToc = "MainWindow.BookBrowser.RenumberToc";

    /// <summary>Saves the selected file to disk.</summary>
    public const string BookBrowserSaveAs = "MainWindow.BookBrowser.SaveAs";

    /// <summary>Selects all files in the folder.</summary>
    public const string BookBrowserSelectAll = "MainWindow.BookBrowser.SelectAll";

    /// <summary>Shows information about the selected files.</summary>
    public const string BookBrowserGetInfo = "MainWindow.BookBrowser.GetInfo";

    /// <summary>Number of Clip Bar slots (the first 60 clips).</summary>
    public const int ClipSlotCount = 60;

    /// <summary>
    /// Action identifier of a Clip Bar slot (1-based, <c>MainWindow.Clip1</c>..<c>MainWindow.Clip60</c>).
    /// </summary>
    public static string Clip(int slot) => "MainWindow.Clip" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="id"/> is a clip slot (<see cref="Clip"/>), and which one.</summary>
    public static bool TryGetClipSlot(string id, out int slot)
    {
        slot = 0;
        return id is not null
            && id.StartsWith("MainWindow.Clip", System.StringComparison.Ordinal)
            && int.TryParse(System.MemoryExtensions.AsSpan(id, "MainWindow.Clip".Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out slot)
            && slot is >= 1 and <= ClipSlotCount;
    }
}
