using System.Collections.Generic;
using System.Linq;
using System;
using Signet.App.Actions;
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
        [ToolbarId.Back] = new[] { AppActionIds.BookmarkLocation, AppActionIds.GoBackFromLinkOrStyle },
        [ToolbarId.Tools] = new[]
        {
            AppActionIds.MetaEditor, AppActionIds.GenerateToc, AppActionIds.EditToc, AppActionIds.SpellcheckEditor,
            // Prettify / Mend the current file.
            Separator, AppActionIds.PrettifyCurrentHtml, AppActionIds.MendCurrentHtml,
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
    /// Toolbars grouped into rows — a new row starts at Heading, Clip Bar and Clip Bar2.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<ToolbarId>> Rows { get; } = BuildRows();

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

    /// <summary>Creates the manager and loads the saved layout (or the default one).</summary>
    public ToolbarManager(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Load();
    }

    /// <summary>Raised after every layout or visibility change (argument = the changed toolbar).</summary>
    public event EventHandler<ToolbarId>? Changed;

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

        _settings.SetStringMap(LayoutGroup, new Dictionary<string, string>());
        _settings.SetStringMap(VisibilityGroup, new Dictionary<string, string>());
        _settings.Save();

        foreach (ToolbarId toolbar in AllToolbars)
        {
            Changed?.Invoke(this, toolbar);
        }
    }

    private void Load()
    {
        IReadOnlyDictionary<string, string> storedLayout = _settings.GetStringMap(LayoutGroup);
        IReadOnlyDictionary<string, string> storedVisibility = _settings.GetStringMap(VisibilityGroup);

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
