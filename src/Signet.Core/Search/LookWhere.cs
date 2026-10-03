namespace Signet.Core.Search;

/// <summary>
/// Find &amp; Replace search scope. The numeric values are fixed because they are persisted
/// as <c>look_where</c> in the settings.
/// </summary>
public enum LookWhere
{
    /// <summary>Only the current file (the active Code View tab).</summary>
    CurrentFile = 0,

    /// <summary>All (X)HTML files in spine order.</summary>
    AllHtmlFiles = 1,

    /// <summary>(X)HTML files selected in the Book Browser.</summary>
    SelectedHtmlFiles = 2,

    /// <summary>(X)HTML files open in tabs.</summary>
    TabbedHtmlFiles = 3,

    /// <summary>All CSS stylesheets.</summary>
    AllCssFiles = 4,

    /// <summary>CSS stylesheets selected in the Book Browser.</summary>
    SelectedCssFiles = 5,

    /// <summary>CSS stylesheets open in tabs.</summary>
    TabbedCssFiles = 6,

    /// <summary>The OPF file.</summary>
    OpfFile = 7,

    /// <summary>The NCX file (if present).</summary>
    NcxFile = 8,

    /// <summary>SVG files selected in the Book Browser.</summary>
    SelectedSvgFiles = 9,

    /// <summary>JavaScript files selected in the Book Browser.</summary>
    SelectedJsFiles = 10,

    /// <summary>Other XML files selected in the Book Browser.</summary>
    SelectedMiscXmlFiles = 11,
}
