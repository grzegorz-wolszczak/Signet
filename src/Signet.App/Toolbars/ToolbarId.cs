namespace Signet.App.Toolbars;

/// <summary>
/// Main window toolbars, in display order.
/// </summary>
public enum ToolbarId
{
    /// <summary>"New": new EPUB2/EPUB3.</summary>
    New,

    /// <summary>"File": Open, Save.</summary>
    File,

    /// <summary>"Add Existing".</summary>
    AddExisting,

    /// <summary>"Undo/Redo".</summary>
    UndoRedo,

    /// <summary>"Edit": Cut, Copy, Paste, Remove Tag Pair.</summary>
    Edit,

    /// <summary>"Find": Find &amp; Replace, Saved Searches.</summary>
    Find,

    /// <summary>"Insert": Split, File, Special Character, ID, Clip, Role, Link.</summary>
    Insert,

    /// <summary>"Back": Bookmark Location, Go Back.</summary>
    Back,

    /// <summary>"Tools": Metadata, Generate/Edit TOC, Spellcheck (+ Prettify/Mend Code for the current file).</summary>
    Tools,

    /// <summary>"Heading": button with an H1–H6/Normal menu. Starts the second row.</summary>
    Heading,

    /// <summary>"Format": B, I, U, S, subscript/superscript.</summary>
    Format,

    /// <summary>"Align".</summary>
    Align,

    /// <summary>"List".</summary>
    List,

    /// <summary>"Indent".</summary>
    Indent,

    /// <summary>"Change Case": button with a letter case menu.</summary>
    ChangeCase,

    /// <summary>"Text Direction" (hidden by default).</summary>
    TextDirection,

    /// <summary>"Clip Bar": clips 1–30 (own row, hidden by default).</summary>
    Clips,

    /// <summary>"Clip Bar2": clips 31–60 (own row, hidden by default).</summary>
    Clips2,
}
