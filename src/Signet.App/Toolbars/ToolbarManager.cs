using System.Collections.Generic;
using System.Linq;
using System;
using Avalonia;
using Avalonia.Media;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.Misc;

namespace Signet.App.Toolbars;

/// <summary>
/// Manages toolbar layout and visibility (toolbars: see <see cref="ToolbarId"/>). Persists the
/// button layout (group <see cref="LayoutGroup"/>: toolbar&#8209;id → comma-separated list of
/// action&#8209;ids) and visibility (group <see cref="VisibilityGroup"/>: toolbar&#8209;id →
/// <c>1</c>/<c>0</c>).
/// </summary>
public sealed class ToolbarManager
{
    /// <summary>
    /// Settings group holding the button layout. The <c>_v2</c> suffix: an older layout used some
    /// of the same toolbar names with different contents, so its entries are ignored rather than
    /// loaded into the new toolbars.
    /// </summary>
    public const string LayoutGroup = "toolbars_v2";

    /// <summary>Settings group holding toolbar visibility (<c>_v2</c> suffix — same as <see cref="LayoutGroup"/>).</summary>
    public const string VisibilityGroup = "toolbars_visible_v2";

    /// <summary>
    /// Settings group holding where the toolbars sit: area name (<c>Top</c>/<c>Bottom</c>) → rows separated by
    /// <c>;</c>, toolbar ids of a row separated by <c>,</c>. Absent when the placement is the default one.
    /// </summary>
    public const string PlacementGroup = "toolbar_rows_v1";

    /// <summary>
    /// Toolbar item: button with a heading level menu (icon <c>heading-all</c>, actions
    /// Heading 1–6 and Normal).
    /// </summary>
    public const string HeadingsMenu = "@headings";

    /// <summary>
    /// Toolbar item: button with a letter case menu (icon <c>case-change</c>, actions
    /// Lowercase/Uppercase/Titlecase/Capitalize).
    /// </summary>
    public const string CaseMenu = "@case";

    private static readonly Dictionary<ToolbarId, string[]> DefaultLayout = new()
    {
        [ToolbarId.New] = new[] { AppActionIds.NewEpub2, AppActionIds.NewEpub3 },
        [ToolbarId.File] = new[] { AppActionIds.Open, AppActionIds.Save },
        [ToolbarId.AddExisting] = new[] { AppActionIds.AddExistingFile },
        [ToolbarId.UndoRedo] = new[] { AppActionIds.Undo, AppActionIds.Redo },
        [ToolbarId.Edit] = new[] { AppActionIds.Cut, AppActionIds.Copy, AppActionIds.Paste, AppActionIds.RemoveTagPair },
        [ToolbarId.Find] = new[] { AppActionIds.Find, AppActionIds.SearchEditor },
        [ToolbarId.Insert] = new[]
        {
            AppActionIds.SplitSection, "|",
            AppActionIds.InsertFile, AppActionIds.InsertSpecialCharacter, AppActionIds.InsertId,
            AppActionIds.InsertClip, AppActionIds.InsertRole, AppActionIds.InsertHyperlink,
        },
        [ToolbarId.Back] = new[] { AppActionIds.BookmarkLocation },
        [ToolbarId.Tools] = new[]
        {
            AppActionIds.MetaEditor, AppActionIds.GenerateToc, AppActionIds.EditToc, AppActionIds.SpellcheckEditor,
            // Prettify / Mend the current file — the HTML pair and, for a stylesheet, Prettify Stylesheet.
            Separator, AppActionIds.PrettifyCurrentHtml, AppActionIds.MendCurrentHtml, AppActionIds.PrettifyCurrentCss,
        },
        [ToolbarId.Heading] = new[] { HeadingsMenu },
        [ToolbarId.Format] = new[]
        {
            AppActionIds.Bold, AppActionIds.Italic, AppActionIds.Underline, AppActionIds.Strikethrough,
            AppActionIds.Subscript, AppActionIds.Superscript,
        },
        [ToolbarId.Align] = new[]
        {
            AppActionIds.AlignLeft, AppActionIds.AlignCenter, AppActionIds.AlignRight, AppActionIds.AlignJustify,
        },
        [ToolbarId.List] = new[] { AppActionIds.InsertBulletedList, AppActionIds.InsertNumberedList },
        [ToolbarId.Indent] = new[] { AppActionIds.DecreaseIndent, AppActionIds.IncreaseIndent },
        [ToolbarId.ChangeCase] = new[] { CaseMenu },
        [ToolbarId.TextDirection] = new[]
        {
            AppActionIds.TextDirectionLtr, AppActionIds.TextDirectionRtl, AppActionIds.TextDirectionDefault,
        },
        // SelectClip at the start of the Clip Bar (see AppActionIds.SelectClip).
        [ToolbarId.Clips] = new[] { AppActionIds.SelectClip, Separator }.Concat(ClipSlotIds(1, 30)).ToArray(),
        [ToolbarId.Clips2] = ClipSlotIds(31, 60).ToArray(),
    };

    private static IEnumerable<string> ClipSlotIds(int from, int to)
    {
        for (int slot = from; slot <= to; slot++)
        {
            yield return AppActionIds.Clip(slot);
        }
    }

    // Hidden by default: Text Direction and the clip bars (Clip Bar2 is empty unless more than
    // 30 clips are defined).
    private static readonly HashSet<ToolbarId> HiddenByDefault = new()
    {
        ToolbarId.TextDirection, ToolbarId.Clips, ToolbarId.Clips2,
    };

    private static readonly Dictionary<ToolbarId, bool> DefaultVisibility =
        Enum.GetValues<ToolbarId>().ToDictionary(id => id, id => !HiddenByDefault.Contains(id));

    // Toolbars that start a new row.
    private static readonly HashSet<ToolbarId> RowBreaks = new() { ToolbarId.Heading, ToolbarId.Clips, ToolbarId.Clips2 };

    /// <summary>Toolbar display name (View → Toolbars menu, customization dialog).</summary>
    public static string DisplayName(ToolbarId toolbar) => toolbar switch
    {
        _ => Strings.TryGet("Toolbar_" + toolbar) ?? toolbar.ToString(),
    };

    /// <summary>
    /// Default placement of the toolbars: all in the <see cref="ToolbarArea.Top"/> area, grouped into rows — a new
    /// row starts at Heading, Clip Bar and Clip Bar2.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ToolbarId>> DefaultRows { get; } = BuildRows();

    private static List<IReadOnlyList<ToolbarId>> BuildRows()
    {
        List<IReadOnlyList<ToolbarId>> rows = new();
        List<ToolbarId> current = new();
        foreach (ToolbarId id in Enum.GetValues<ToolbarId>())
        {
            if (RowBreaks.Contains(id) && current.Count > 0)
            {
                rows.Add(current);
                current = new List<ToolbarId>();
            }

            current.Add(id);
        }

        rows.Add(current);
        return rows;
    }

    /// <summary>Separator in a toolbar layout.</summary>
    public const string Separator = "|";

    private readonly SettingsStore _settings;
    private readonly Dictionary<ToolbarId, List<string>> _layout = new();
    private readonly Dictionary<ToolbarId, bool> _visible = new();
    private readonly Dictionary<ToolbarArea, List<List<ToolbarId>>> _placement = new();

    /// <summary>Application resource holding the size (px) of the icons of the main window's toolbars.</summary>
    public const string IconSizeResourceKey = "SignetMainToolbarIconSize";

    /// <summary>Application resource holding the font family of the toolbar button texts (absent = the interface font).</summary>
    public const string FontFamilyResourceKey = "SignetToolbarFontFamily";

    /// <summary>Application resource holding the font size of the toolbar button texts (absent = the default).</summary>
    public const string FontSizeResourceKey = "SignetToolbarFontSize";

    /// <summary>Application resource holding the font weight of the toolbar button texts (absent = not set).</summary>
    public const string FontWeightResourceKey = "SignetToolbarFontWeight";

    /// <summary>
    /// Creates the manager, loads the saved layout (or the default one) and applies the saved look
    /// (<see cref="Appearance"/>) to the application resources.
    /// </summary>
    public ToolbarManager(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Load();
        _settings.SettingChanged += OnSettingChanged;
        ApplyAppearance();
    }

    /// <summary>
    /// Raised after the look of the toolbars changed (<see cref="Appearance"/>) — from a toolbar's context menu, from
    /// Preferences (once applied) or by restoring the appearance defaults. The resources are already updated.
    /// </summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>The look of the toolbars, shared by all of them (kept in the settings).</summary>
    public ToolbarAppearance Appearance => _settings.ToolbarAppearance;

    /// <summary>Sets where the buttons show their text (saved at once).</summary>
    public void SetTextPosition(ToolbarTextPosition position) => SetAppearance(Appearance with { TextPosition = position });

    /// <summary>Sets the size of the icons (saved at once).</summary>
    public void SetIconSize(ToolbarIconSize size) => SetAppearance(Appearance with { IconSize = size });

    /// <summary>Locks or unlocks the positions of the toolbars: locked toolbars have no grip (saved at once).</summary>
    public void SetPositionsLocked(bool locked) => SetAppearance(Appearance with { PositionsLocked = locked });

    /// <summary>Puts the look of the toolbars into the application resources (the toolbar views take it from there).</summary>
    public void ApplyAppearance()
    {
        if (Application.Current is { } app)
        {
            ApplyResources(app, Appearance);
        }
    }

    /// <summary>
    /// Sets the resources of the toolbars' look: the icon size always, the font of the button texts only for what is
    /// set (what is left empty / 0 is no resource, so the text keeps the font of the buttons).
    /// </summary>
    public static void ApplyResources(Application app, ToolbarAppearance look)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Resources[IconSizeResourceKey] = (double)look.IconPixels;
        if (look.FontFamily.Length > 0)
        {
            app.Resources[FontFamilyResourceKey] = FontFamily.Parse(look.FontFamily);
        }
        else
        {
            app.Resources.Remove(FontFamilyResourceKey);
        }

        if (look.FontSize > 0)
        {
            app.Resources[FontSizeResourceKey] = (double)look.FontSize;
        }
        else
        {
            app.Resources.Remove(FontSizeResourceKey);
        }

        if (look.FontWeight > 0)
        {
            app.Resources[FontWeightResourceKey] = FontWeightOptions.ToAvalonia(look.FontWeight);
        }
        else
        {
            app.Resources.Remove(FontWeightResourceKey);
        }
    }

    private void SetAppearance(ToolbarAppearance look)
    {
        if (look == Appearance)
        {
            return;
        }

        // The settings raise SettingChanged, which applies the look (OnSettingChanged).
        _settings.ToolbarAppearance = look;
        _settings.Save();
    }

    // The look changes from this manager, from Preferences (the draft committed) and with the appearance defaults restored.
    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.QualifiedKey.EndsWith("/toolbar_appearance", StringComparison.Ordinal)
            || e.QualifiedKey.EndsWith("/appearance_settings_cleared", StringComparison.Ordinal))
        {
            ApplyAppearance();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised after every layout or visibility change (argument = the changed toolbar).</summary>
    public event EventHandler<ToolbarId>? Changed;

    /// <summary>Raised after toolbars were moved between rows or areas (<see cref="GetRows"/> returns the new placement).</summary>
    public event EventHandler? PlacementChanged;

    /// <summary>
    /// The rows of toolbars in <paramref name="area"/>, top to bottom, each row left to right. A row can hold only
    /// hidden toolbars; an area without rows is empty.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ToolbarId>> GetRows(ToolbarArea area) =>
        _placement[area].Select(row => (IReadOnlyList<ToolbarId>)row.ToList()).ToList();

    /// <summary>
    /// Moves <paramref name="toolbar"/> into the existing row <paramref name="rowIndex"/> of <paramref name="area"/>,
    /// before <paramref name="before"/> (<see langword="null"/>, or a toolbar that is not in that row = at the end of
    /// the row). A row left empty by the move disappears.
    /// </summary>
    /// <returns><see langword="true"/> when the placement changed.</returns>
    public bool MoveToolbar(ToolbarId toolbar, ToolbarArea area, int rowIndex, ToolbarId? before)
    {
        List<List<ToolbarId>> rows = _placement[area];
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rowIndex, rows.Count);
        if (before == toolbar)
        {
            return false;
        }

        string snapshot = SerializePlacement();
        List<ToolbarId> target = rows[rowIndex];
        RemoveFromRows(toolbar, except: null);
        int at = before is { } anchor ? target.IndexOf(anchor) : -1;
        target.Insert(at < 0 ? target.Count : at, toolbar);
        return CommitPlacement(snapshot);
    }

    /// <summary>
    /// Moves <paramref name="toolbar"/> into a new row inserted at <paramref name="rowIndex"/> of
    /// <paramref name="area"/> (0 = above the first row, the row count = below the last one; larger values are
    /// treated as the row count). A row left empty by the move disappears.
    /// </summary>
    /// <returns><see langword="true"/> when the placement changed.</returns>
    public bool MoveToolbarToNewRow(ToolbarId toolbar, ToolbarArea area, int rowIndex)
    {
        List<List<ToolbarId>> rows = _placement[area];
        string snapshot = SerializePlacement();
        List<ToolbarId> row = new() { toolbar };
        rows.Insert(Math.Clamp(rowIndex, 0, rows.Count), row);
        RemoveFromRows(toolbar, except: row);
        return CommitPlacement(snapshot);
    }

    /// <summary>All toolbar identifiers in a fixed order.</summary>
    public static IReadOnlyList<ToolbarId> AllToolbars { get; } =
        Enum.GetValues<ToolbarId>();

    /// <summary>Toolbar items (action ids or <see cref="Separator"/>).</summary>
    public IReadOnlyList<string> GetItems(ToolbarId toolbar) => _layout[toolbar];

    /// <summary>Default toolbar layout (for reset / the editor).</summary>
    public static IReadOnlyList<string> GetDefaultItems(ToolbarId toolbar) => DefaultLayout[toolbar];

    /// <summary>Whether the toolbar is visible.</summary>
    public bool IsVisible(ToolbarId toolbar) => _visible[toolbar];

    /// <summary>Sets toolbar visibility and persists it.</summary>
    public void SetVisible(ToolbarId toolbar, bool visible)
    {
        if (_visible[toolbar] == visible)
        {
            return;
        }

        _visible[toolbar] = visible;
        PersistVisibility();
        Changed?.Invoke(this, toolbar);
    }

    /// <summary>Toggles toolbar visibility.</summary>
    public void ToggleVisible(ToolbarId toolbar) => SetVisible(toolbar, !_visible[toolbar]);

    /// <summary>
    /// Whether all toolbars are hidden (View › Toolbars › Hide All Toolbars): the toolbar areas take no space, while
    /// each toolbar keeps its own visibility (<see cref="IsVisible"/>) for when they are shown again. Saved at once.
    /// </summary>
    public bool AllHidden
    {
        get => _settings.ToolbarsAllHidden;
        set
        {
            if (_settings.ToolbarsAllHidden == value)
            {
                return;
            }

            _settings.ToolbarsAllHidden = value;
            _settings.Save();
            AllHiddenChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised after <see cref="AllHidden"/> changed.</summary>
    public event EventHandler? AllHiddenChanged;

    /// <summary>Replaces the toolbar layout with a new item list (action ids or separators) and persists it.</summary>
    public void SetItems(ToolbarId toolbar, IEnumerable<string> items)
    {
        _layout[toolbar] = items.ToList();
        PersistLayout();
        Changed?.Invoke(this, toolbar);
    }

    /// <summary>Restores the factory layout and visibility of all toolbars.</summary>
    public void ResetToDefaults()
    {
        foreach (ToolbarId toolbar in AllToolbars)
        {
            _layout[toolbar] = DefaultLayout[toolbar].ToList();
            _visible[toolbar] = DefaultVisibility[toolbar];
        }

        LoadPlacement(new Dictionary<string, string>());

        _settings.SetStringMap(LayoutGroup, new Dictionary<string, string>());
        _settings.SetStringMap(VisibilityGroup, new Dictionary<string, string>());
        _settings.SetStringMap(PlacementGroup, new Dictionary<string, string>());
        _settings.Save();

        foreach (ToolbarId toolbar in AllToolbars)
        {
            Changed?.Invoke(this, toolbar);
        }

        PlacementChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CommitPlacement(string before)
    {
        foreach (List<List<ToolbarId>> rows in _placement.Values)
        {
            rows.RemoveAll(row => row.Count == 0);
        }

        if (SerializePlacement() == before)
        {
            return false;
        }

        PersistPlacement();
        PlacementChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void RemoveFromRows(ToolbarId toolbar, List<ToolbarId>? except)
    {
        foreach (List<List<ToolbarId>> rows in _placement.Values)
        {
            foreach (List<ToolbarId> row in rows)
            {
                if (!ReferenceEquals(row, except))
                {
                    row.Remove(toolbar);
                }
            }
        }
    }

    // One line per area: rows separated by ';', toolbars of a row by ','. Also used to compare two placements.
    private string SerializePlacement() =>
        string.Join('|', Enum.GetValues<ToolbarArea>().Select(SerializeArea));

    private string SerializeArea(ToolbarArea area) =>
        string.Join(';', _placement[area].Select(row => string.Join(',', row)));

    private void LoadPlacement(IReadOnlyDictionary<string, string> stored)
    {
        foreach (ToolbarArea area in Enum.GetValues<ToolbarArea>())
        {
            _placement[area] = new List<List<ToolbarId>>();
        }

        if (stored.Count == 0)
        {
            _placement[ToolbarArea.Top] = DefaultRows.Select(row => row.ToList()).ToList();
            return;
        }

        // Unknown and repeated ids are dropped (a stale file must not lose or double a toolbar).
        HashSet<ToolbarId> placed = new();
        foreach (ToolbarArea area in Enum.GetValues<ToolbarArea>())
        {
            if (!stored.TryGetValue(area.ToString(), out string? raw))
            {
                continue;
            }

            foreach (string rowText in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                List<ToolbarId> row = new();
                foreach (string name in rowText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Enum.TryParse(name, out ToolbarId id) && Enum.IsDefined(id) && placed.Add(id))
                    {
                        row.Add(id);
                    }
                }

                if (row.Count > 0)
                {
                    _placement[area].Add(row);
                }
            }
        }

        // Toolbars missing from the file (e.g. added by a newer version) go to the end of the last top row.
        List<ToolbarId> missing = AllToolbars.Where(id => !placed.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            List<List<ToolbarId>> top = _placement[ToolbarArea.Top];
            if (top.Count == 0)
            {
                top.Add(new List<ToolbarId>());
            }

            top[^1].AddRange(missing);
        }
    }

    private void PersistPlacement()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        string defaults = string.Join(';', DefaultRows.Select(row => string.Join(',', row)));
        if (SerializeArea(ToolbarArea.Top) != defaults || _placement[ToolbarArea.Bottom].Count > 0)
        {
            foreach (ToolbarArea area in Enum.GetValues<ToolbarArea>())
            {
                if (_placement[area].Count > 0)
                {
                    map[area.ToString()] = SerializeArea(area);
                }
            }
        }

        _settings.SetStringMap(PlacementGroup, map);
        _settings.Save();
    }

    private void Load()
    {
        IReadOnlyDictionary<string, string> storedLayout = _settings.GetStringMap(LayoutGroup);
        IReadOnlyDictionary<string, string> storedVisibility = _settings.GetStringMap(VisibilityGroup);
        LoadPlacement(_settings.GetStringMap(PlacementGroup));

        foreach (ToolbarId toolbar in AllToolbars)
        {
            _layout[toolbar] = storedLayout.TryGetValue(toolbar.ToString(), out string? raw)
                ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : DefaultLayout[toolbar].ToList();

            _visible[toolbar] = storedVisibility.TryGetValue(toolbar.ToString(), out string? v)
                ? v == "1"
                : DefaultVisibility[toolbar];
        }
    }

    private void PersistLayout()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (ToolbarId toolbar in AllToolbars)
        {
            if (!_layout[toolbar].SequenceEqual(DefaultLayout[toolbar]))
            {
                map[toolbar.ToString()] = string.Join(',', _layout[toolbar]);
            }
        }

        _settings.SetStringMap(LayoutGroup, map);
        _settings.Save();
    }

    private void PersistVisibility()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (ToolbarId toolbar in AllToolbars)
        {
            if (_visible[toolbar] != DefaultVisibility[toolbar])
            {
                map[toolbar.ToString()] = _visible[toolbar] ? "1" : "0";
            }
        }

        _settings.SetStringMap(VisibilityGroup, map);
        _settings.Save();
    }
}
