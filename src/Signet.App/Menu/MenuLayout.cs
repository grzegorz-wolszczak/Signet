using System.Collections.Generic;
using Signet.App.Actions;

namespace Signet.App.Menu;

/// <summary>Menu description element: a separator, a submenu or a reference to an action.</summary>
public abstract record MenuNode
{
    /// <summary>Separator.</summary>
    public static MenuNode Separator { get; } = new SeparatorNode();

    /// <summary>Reference to an action by identifier.</summary>
    public static MenuNode Action(string id) => new ActionNode(id);

    /// <summary>Submenu with a header (with an <c>&amp;</c> mnemonic).</summary>
    public static MenuNode Sub(string header, params MenuNode[] children) => new SubmenuNode(header, children);

    /// <summary>
    /// Dynamic flat list of recent files inserted directly into the menu —
    /// filled by <c>MenuBuilder</c>; an empty list leaves no item behind.
    /// </summary>
    public static MenuNode RecentFiles { get; } = new RecentFilesNode();

    /// <summary>Dynamic "Bookmarks" submenu — filled from the session bookmarks list.</summary>
    public static MenuNode Bookmarks(string header) => new BookmarksNode(header);

    internal sealed record SeparatorNode : MenuNode;

    internal sealed record ActionNode(string Id) : MenuNode;

    internal sealed record SubmenuNode(string Header, IReadOnlyList<MenuNode> Children) : MenuNode;

    internal sealed record RecentFilesNode : MenuNode;

    internal sealed record BookmarksNode(string Header) : MenuNode;
}

/// <summary>
/// Static structure of the main menu.
/// </summary>
public static class MenuLayout
{
    private static MenuNode A(string id) => MenuNode.Action(id);

    private static MenuNode Sep => MenuNode.Separator;

    // The Clip / Clip2 menus (clips 1..30 / 31..60 — see AppActionIds.ClipSlotCount).
    private static MenuNode[] ClipRange(int from, int to)
    {
        var nodes = new List<MenuNode>(to - from + 1);
        for (int slot = from; slot <= to; slot++)
        {
            nodes.Add(A(AppActionIds.Clip(slot)));
        }

        return nodes.ToArray();
    }

    /// <summary>The main menu, in display order.</summary>
    public static IReadOnlyList<MenuNode> TopLevel { get; } = new[]
    {
        MenuNode.Sub("&File",
            MenuNode.Sub("&New",
                A("MainWindow.NewDefault"), A("MainWindow.NewEpub2"), A("MainWindow.NewEpub3")),
            A("MainWindow.Open"),
            Sep,
            MenuNode.Sub("A&dd",
                A("MainWindow.AddExistingFile"),
                Sep,
                A("MainWindow.NewHTMLFile"), A("MainWindow.NewCSSFile"),
                A("MainWindow.NewJSFile"), A("MainWindow.NewSVGFile")),
            Sep,
            A("MainWindow.Save"), A("MainWindow.SaveAs"), A("MainWindow.SaveACopy"),
            Sep,
            A("MainWindow.PrintPreview"), A("MainWindow.Print"),
            Sep,
            // Recent files right above Close/Exit.
            MenuNode.RecentFiles,
            A("MainWindow.Close"), A("MainWindow.Exit")),

        MenuNode.Sub("&Edit",
            A("MainWindow.Undo"), A("MainWindow.Redo"),
            Sep,
            A("MainWindow.RevertToBefore"), A("MainWindow.RevertToAfter"), A("MainWindow.CreateCheckpoint"),
            Sep,
            A("MainWindow.Cut"), A("MainWindow.Copy"), A("MainWindow.Paste"),
            A("MainWindow.PasteClipboardHistory"), A("MainWindow.DeleteLine"),
            Sep,
            MenuNode.Sub("C&hange Case",
                A("MainWindow.CasingLowercase"), A("MainWindow.CasingUppercase"),
                A("MainWindow.CasingTitlecase"), A("MainWindow.CasingCapitalize")),
            Sep,
            A("MainWindow.SplitSection"), A("MainWindow.SplitOnSGFSectionMarkers"),
            Sep,
            A("MainWindow.Preferences"),
            Sep,
            A("MainWindow.MergeContent")),

        MenuNode.Sub("&Insert",
            A("MainWindow.InsertSGFSectionMarker"),
            Sep,
            A("MainWindow.InsertFile"), A("MainWindow.InsertSpecialCharacter"),
            Sep,
            A("MainWindow.InsertId"), A("MainWindow.InsertClip"),
            A("MainWindow.InsertRole"), A("MainWindow.InsertHyperlink"),
            Sep,
            A("MainWindow.InsertClosingTag"),
            Sep,
            MenuNode.Sub("Cli&p", ClipRange(1, 30)),
            MenuNode.Sub("Cli&p2", ClipRange(31, 60))),

        MenuNode.Sub("For&mat",
            MenuNode.Sub("&Heading",
                A("MainWindow.Heading1"), A("MainWindow.Heading2"), A("MainWindow.Heading3"),
                A("MainWindow.Heading4"), A("MainWindow.Heading5"), A("MainWindow.Heading6"),
                A("MainWindow.HeadingNormal"),
                Sep,
                A("MainWindow.HeadingPreserveAttributes")),
            Sep,
            A("MainWindow.Bold"), A("MainWindow.Italic"), A("MainWindow.Underline"),
            A("MainWindow.Strikethrough"), A("MainWindow.Subscript"), A("MainWindow.Superscript"),
            Sep,
            A("MainWindow.AlignLeft"), A("MainWindow.AlignCenter"),
            A("MainWindow.AlignRight"), A("MainWindow.AlignJustify"),
            Sep,
            A("MainWindow.InsertBulletedList"), A("MainWindow.InsertNumberedList"),
            Sep,
            A("MainWindow.DecreaseIndent"), A("MainWindow.IncreaseIndent"),
            Sep,
            A("MainWindow.TextDirectionLTR"), A("MainWindow.TextDirectionRTL"), A("MainWindow.TextDirectionDefault"),
            Sep,
            A("MainWindow.RemoveFormatting"), A("MainWindow.RemoveTagPair"),
            Sep,
            A("MainWindow.JumpToOpeningTag"), A("MainWindow.JumpToClosingTag"), A("MainWindow.SelectTagContents"),
            A("MainWindow.RenameTag"), A("MainWindow.SplitTag")),

        MenuNode.Sub("&Search",
            A("MainWindow.Find"), A("MainWindow.HideFind"),
            Sep,
            A("MainWindow.FindNext"), A("MainWindow.FindPrevious"),
            Sep,
            A("MainWindow.ReplaceCurrent"), A("MainWindow.ReplaceNext"),
            A("MainWindow.ReplacePrevious"), A("MainWindow.ReplaceAll"),
            Sep,
            A("MainWindow.Count"), A("MainWindow.DryRunReplaceAll"),
            A("MainWindow.FilterReplaceAll"), A("MainWindow.RestartSearch"),
            Sep,
            MenuNode.Sub("Current Fil&e",
                A("MainWindow.FindNextInFile"),
                Sep,
                A("MainWindow.ReplaceNextInFile"), A("MainWindow.ReplaceAllInFile"),
                Sep,
                A("MainWindow.CountInFile")),
            Sep,
            A("MainWindow.BookmarkLocation"), A("MainWindow.GoToLinkOrStyle"), A("MainWindow.GoBackFromLinkOrStyle"),
            Sep,
            A("MainWindow.MarkSelection"), A("MainWindow.GoToLine")),

        MenuNode.Sub("&Tools",
            A("MainWindow.AddCover"), A("MainWindow.MetaEditor"),
            MenuNode.Sub("&Table Of Contents",
                A("MainWindow.GenerateTOC"), A("MainWindow.EditTOC"), A("MainWindow.CreateHTMLTOC")),
            A("MainWindow.StandardizeEpub"), A("MainWindow.UseStandardFileExtensions"),
            A("MainWindow.RebaseManifestIDs"), A("MainWindow.UpdateManifestMediaTypes"), A("MainWindow.CustomLayout"),
            Sep,
            MenuNode.Sub("Spe&llcheck",
                A("MainWindow.SpellcheckEditor"),
                Sep,
                A("MainWindow.AutoSpellCheck"), A("MainWindow.Spellcheck"),
                Sep,
                A("MainWindow.AddMispelledWord"), A("MainWindow.IgnoreMispelledWord"),
                Sep,
                A("MainWindow.ClearIgnoredWords")),
            MenuNode.Sub("Re&format HTML",
                A("MainWindow.MendPrettifyHTML"), A("MainWindow.MendHTML")),
            MenuNode.Sub("Epub&3 Tools",
                A("MainWindow.UpdateManifestProperties"), A("MainWindow.NCXGuideFromNav"),
                A("MainWindow.RemoveNCXGuide"), A("MainWindow.RemoveNavFromGuide"),
                A("MainWindow.AddNavToSpine"), A("MainWindow.AddNavToSpineNonLinear")),
            A("MainWindow.WellFormedCheckEpub"), A("MainWindow.ValidateStylesheetsWithW3C"), A("MainWindow.Reports"),
            Sep,
            A("MainWindow.ClipEditor"), A("MainWindow.SelectClip"), A("MainWindow.SearchEditor"),
            Sep,
            A("MainWindow.DeleteUnusedMedia"), A("MainWindow.DeleteUnusedStyles"), A("MainWindow.CssCleanup"),
            A("MainWindow.AddSoftHyphens"), A("MainWindow.RemoveSoftHyphens"),
            Sep, A("MainWindow.LiveCssPanel")),

        MenuNode.Sub("&View",
            MenuNode.Sub("&Toolbars"),
            Sep,
            A("MainWindow.ZoomIn"), A("MainWindow.ZoomOut"), A("MainWindow.ZoomReset"),
            Sep,
            A("MainWindow.WordWrap"),
            Sep,
            A("MainWindow.BookBrowser"), A("MainWindow.ClipsWindow"), A("MainWindow.PreviewWindow"),
            A("MainWindow.TableOfContents"), A("MainWindow.ValidationResults"), A("MainWindow.CheckpointsWindow"),
            A("MainWindow.FindReplaceWindow"),
            Sep,
            A("MainWindow.FocusOnBookBrowser"), A("MainWindow.FocusOnCodeView"), A("MainWindow.FocusOnPreview"),
            A("MainWindow.FocusOnTOC"), A("MainWindow.FocusOnClips")),

        MenuNode.Sub("&Window",
            A("MainWindow.NextTab"), A("MainWindow.PreviousTab"),
            A("MainWindow.CloseTab"), A("MainWindow.CloseOtherTabs"),
            Sep,
            A("MainWindow.PreviousResource"), A("MainWindow.NextResource"),
            Sep,
            MenuNode.Bookmarks("&Bookmarks")),

        MenuNode.Sub("&Help",
            A("MainWindow.About")),
    };
}
