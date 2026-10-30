using System.Collections.Generic;
using Signet.Core.Themes;

namespace Signet.App.ViewModels;

// The colors of the theme that only one part of the application uses (ThemeKey.OwnedByPanel): edited on that part's own
// page of Preferences, next to its other settings, not in the shared Interface Colors tree of Appearance (which keeps
// only what the whole application shares). The Notifications page has its own (PreferencesViewModel.Notifications.cs).
public sealed partial class PreferencesViewModel
{
    private readonly Dictionary<(ThemeKey Key, TextAttributesColor? Part), ColorKeyEditorViewModel> _panelColors = new();

    /// <summary>Appearance › Start Screen: the name of a recent file.</summary>
    public ColorKeyEditorViewModel StartScreenTitleColor => PanelColor(ThemeKeys.UiStartScreenTitle);

    /// <summary>Appearance › Start Screen: the folder of a recent file.</summary>
    public ColorKeyEditorViewModel StartScreenLocationColor => PanelColor(ThemeKeys.UiStartScreenLocation);

    /// <summary>Appearance › Start Screen: the background of the recent file under the pointer.</summary>
    public ColorKeyEditorViewModel StartScreenHighlightColor => PanelColor(ThemeKeys.UiStartScreenHighlight);

    /// <summary>Appearance › Context Menus: the background of the title.</summary>
    public ColorKeyEditorViewModel ContextMenuTitleBackgroundColor => PanelColor(ThemeKeys.UiContextMenuTitleBackground);

    /// <summary>Appearance › Book Browser: the background of the tree.</summary>
    public ColorKeyEditorViewModel BookBrowserBackgroundColor => PanelColor(ThemeKeys.UiBookBrowserBackground);

    /// <summary>Appearance › Book Browser: the text of the selected row.</summary>
    public ColorKeyEditorViewModel BookBrowserSelectionForegroundColor =>
        PanelColor(ThemeKeys.UiBookBrowserSelection, TextAttributesColor.Foreground);

    /// <summary>Appearance › Book Browser: the background of the selected row.</summary>
    public ColorKeyEditorViewModel BookBrowserSelectionBackgroundColor =>
        PanelColor(ThemeKeys.UiBookBrowserSelection, TextAttributesColor.Background);

    /// <summary>Find Usages Window: the background of the "Usage" column.</summary>
    public ColorKeyEditorViewModel FindUsagesUsageBackgroundColor => PanelColor(ThemeKeys.UiFindUsagesUsageBackground);

    /// <summary>Find Usages Window: the background of the "Line" column.</summary>
    public ColorKeyEditorViewModel FindUsagesLineBackgroundColor => PanelColor(ThemeKeys.UiFindUsagesLineBackground);

    /// <summary>Find Usages Window: the background of the "Col." column.</summary>
    public ColorKeyEditorViewModel FindUsagesColumnBackgroundColor => PanelColor(ThemeKeys.UiFindUsagesColumnBackground);

    /// <summary>Find Usages Window: the background of the "Kind" column.</summary>
    public ColorKeyEditorViewModel FindUsagesKindBackgroundColor => PanelColor(ThemeKeys.UiFindUsagesKindBackground);

    /// <summary>Find Usages Window: the background of the "Context" column.</summary>
    public ColorKeyEditorViewModel FindUsagesContextBackgroundColor => PanelColor(ThemeKeys.UiFindUsagesContextBackground);

    /// <summary>Saved Searches: the background of the tree.</summary>
    public ColorKeyEditorViewModel SearchEditorBackgroundColor => PanelColor(ThemeKeys.UiSearchEditorBackground);

    // One editor per key (and color of a text attributes key), made when its page first asks for it.
    private ColorKeyEditorViewModel PanelColor(ThemeKey key, TextAttributesColor? part = null)
    {
        if (!_panelColors.TryGetValue((key, part), out ColorKeyEditorViewModel? editor))
        {
            editor = new ColorKeyEditorViewModel(Theme, key, part);
            _panelColors[(key, part)] = editor;
        }

        return editor;
    }
}
