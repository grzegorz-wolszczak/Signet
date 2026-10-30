using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Toolbars;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// A single toolbar item: an action button, a separator or a button with a menu
/// (the heading and case buttons).
/// </summary>
public sealed partial class ToolbarItemViewModel : ObservableObject
{
    private readonly string? _dropDownIconKey;
    private readonly string? _dropDownToolTip;
    private readonly string? _dropDownLabel;

    private ToolbarItemViewModel(AppAction? action, bool isSeparator)
    {
        Action = action;
        IsSeparator = isSeparator;
        MenuActions = System.Array.Empty<AppAction>();
        TriggerDefaultCommand = new RelayCommand(TriggerDefault);
        ChooseCommand = new RelayCommand<AppAction>(Choose);
    }

    private ToolbarItemViewModel(IReadOnlyList<AppAction> menuActions, string iconKey, string label, string toolTip)
        : this(null, isSeparator: false)
    {
        MenuActions = menuActions;
        _dropDownIconKey = iconKey;
        _dropDownLabel = label;
        _dropDownToolTip = toolTip;
    }

    /// <summary>Separator.</summary>
    public static ToolbarItemViewModel Separator { get; } = new(null, isSeparator: true);

    /// <summary>A button bound to an action.</summary>
    public static ToolbarItemViewModel ForAction(AppAction action) => new(action, isSeparator: false);

    /// <summary>
    /// A button with an action menu. The main part runs the most recently chosen action,
    /// the arrow opens the menu.
    /// </summary>
    public static ToolbarItemViewModel ForDropDown(
        IReadOnlyList<AppAction> menuActions, string iconKey, string label, string toolTip) =>
        new(menuActions, iconKey, label, toolTip);

    /// <summary>The button's action (or <see langword="null"/> for a separator / a button with a menu).</summary>
    public AppAction? Action { get; }

    /// <summary>Whether this is a separator.</summary>
    public bool IsSeparator { get; }

    /// <summary>Whether this is a button with a menu (<see cref="MenuActions"/>).</summary>
    public bool IsDropDown => MenuActions.Count > 0;

    /// <summary>Whether this is a plain action button (neither a separator nor a button with a menu).</summary>
    public bool IsButton => !IsSeparator && !IsDropDown;

    /// <summary>Menu actions of a button with a menu (an empty list for other items).</summary>
    public IReadOnlyList<AppAction> MenuActions { get; }

    /// <summary>
    /// The most recently chosen action of a button with a menu — run by clicking the main part; its
    /// icon replaces the button's icon.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKey))]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    private AppAction? _defaultAction;

    /// <summary>The button's command (for a button with a menu: running the default action).</summary>
    public ICommand? Command => IsDropDown ? TriggerDefaultCommand : Action;

    /// <summary>Runs the default action of a button with a menu.</summary>
    public IRelayCommand TriggerDefaultCommand { get; }

    /// <summary>Choosing an action from the menu: it becomes the default and is run.</summary>
    public IRelayCommand<AppAction> ChooseCommand { get; }

    /// <summary>Raised when the main part of a button with a menu has no default action yet.</summary>
    public event System.EventHandler? MenuRequested;

    /// <summary>Short button label (visible only for items without an icon).</summary>
    public string Label => IsDropDown
        ? _dropDownLabel ?? string.Empty
        : Action is null ? string.Empty : StripMnemonics(Action.DefaultText);

    /// <summary>
    /// The <see cref="Label"/> as the button shows it: a long one in two lines (<see cref="WrapLabel"/>), so that the
    /// text does not make the button much wider than its icon.
    /// </summary>
    public string DisplayLabel => WrapLabel(Label);

    /// <summary>Labels up to this many characters stay in one line.</summary>
    public const int SingleLineLabelMax = 16;

    /// <summary>
    /// Breaks a label longer than <see cref="SingleLineLabelMax"/> into two lines at the space that gives the most even
    /// lines ("Mend Code in Current File" → "Mend Code in" / "Current File"); a shorter label, or one without a space,
    /// stays as it is.
    /// </summary>
    public static string WrapLabel(string label)
    {
        System.ArgumentNullException.ThrowIfNull(label);
        if (label.Length <= SingleLineLabelMax)
        {
            return label;
        }

        int best = -1;
        int bestLongest = int.MaxValue;
        for (int i = label.IndexOf(' ', System.StringComparison.Ordinal); i >= 0; i = label.IndexOf(' ', i + 1))
        {
            int longest = System.Math.Max(i, label.Length - i - 1);
            if (longest < bestLongest)
            {
                best = i;
                bestLongest = longest;
            }
        }

        return best < 0 ? label : label[..best] + "\n" + label[(best + 1)..];
    }

    /// <summary>
    /// Icon key — the name of a resource merged by
    /// <see cref="Signet.App.Infrastructure.IconThemeManager"/>, or <c>null</c> when the catalog
    /// assigned no icon (the button then shows just the <see cref="Label"/>).
    /// </summary>
    public string? IconKey => IsDropDown ? DefaultAction?.IconKey ?? _dropDownIconKey : Action?.IconKey;

    /// <summary>Tooltip (the action's full text + shortcut).</summary>
    public string ToolTip
    {
        get
        {
            if (IsDropDown)
            {
                return DefaultAction is null ? _dropDownToolTip ?? string.Empty : ActionToolTip(DefaultAction);
            }

            return Action is null ? string.Empty : ActionToolTip(Action);
        }
    }

    private static string ActionToolTip(AppAction action)
    {
        string label = StripMnemonics(action.DefaultText);
        return action.InputGestureText.Length > 0 ? $"{label} ({action.InputGestureText})" : label;
    }

    private void TriggerDefault()
    {
        if (DefaultAction is { } action)
        {
            if (action.CanExecute(null))
            {
                action.Execute(null);
            }

            return;
        }

        MenuRequested?.Invoke(this, System.EventArgs.Empty);
    }

    private void Choose(AppAction? action)
    {
        if (action is null)
        {
            return;
        }

        DefaultAction = action;
        if (action.CanExecute(null))
        {
            action.Execute(null);
        }
    }

    private static string StripMnemonics(string text) =>
        text.Replace("&&", "\0").Replace("&", string.Empty).Replace("\0", "&").TrimEnd('.', '…');
}

/// <summary>View model of a single toolbar.</summary>
public sealed partial class ToolbarViewModel : ObservableObject
{
    private readonly ToolbarManager _manager;
    private readonly AppActionRegistry _actions;

    public ToolbarViewModel(ToolbarId id, ToolbarManager manager, AppActionRegistry actions)
    {
        Id = id;
        _manager = manager;
        _actions = actions;
        _name = ToolbarManager.DisplayName(id);
        _isVisible = manager.IsVisible(id);
        _appearance = manager.Appearance;
        Rebuild();
    }

    /// <summary>Toolbar identifier.</summary>
    public ToolbarId Id { get; }

    /// <summary>Toolbar name.</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>Whether the toolbar is visible.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Toolbar items.</summary>
    public ObservableCollection<ToolbarItemViewModel> Items { get; } = new();

    /// <summary>Recomputes the toolbar's name and items after the UI language changes.</summary>
    public void RefreshTexts()
    {
        Name = ToolbarManager.DisplayName(Id);
        Rebuild();
    }

    /// <summary>Refreshes the state from <see cref="ToolbarManager"/>.</summary>
    public void Refresh()
    {
        IsVisible = _manager.IsVisible(Id);
        Rebuild();
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (string entry in _manager.GetItems(Id))
        {
            if (entry == ToolbarManager.Separator)
            {
                Items.Add(ToolbarItemViewModel.Separator);
                continue;
            }

            if (entry == ToolbarManager.HeadingsMenu)
            {
                Items.Add(ToolbarItemViewModel.ForDropDown(
                    Resolve(HeadingActionIds), "heading-all", "h*", Strings.Get("Toolbar_HeadingMenuTip")));
                continue;
            }

            if (entry == ToolbarManager.CaseMenu)
            {
                Items.Add(ToolbarItemViewModel.ForDropDown(
                    Resolve(CaseActionIds), "case-change", "Aa", Strings.Get("Toolbar_CaseMenuTip")));
                continue;
            }

            AppAction? action = _actions.Get(entry);
            if (action is not null)
            {
                Items.Add(ToolbarItemViewModel.ForAction(action));
            }
        }
    }

    partial void OnIsVisibleChanged(bool value) => _manager.SetVisible(Id, value);

    /// <summary>Raised when the user chooses "Configure Toolbars…" in the toolbar's context menu.</summary>
    public event System.EventHandler? ConfigureRequested;

    /// <summary>Hides the toolbar (as unchecking it in View → Toolbars does).</summary>
    [RelayCommand]
    private void Hide() => IsVisible = false;

    /// <summary>
    /// Asks for the window that configures the toolbars' buttons (<see cref="ConfigureRequested"/>), opened on this
    /// toolbar.
    /// </summary>
    [RelayCommand]
    private void Configure() => ConfigureRequested?.Invoke(this, System.EventArgs.Empty);

    // ---- the look shared by all toolbars (ToolbarManager.Appearance): the "Toolbar Settings" section of the menu ----

    private ToolbarAppearance _appearance;

    /// <summary>The buttons show icons only (a button without an icon shows its text).</summary>
    public bool IsIconsOnly => _appearance.TextPosition == ToolbarTextPosition.IconsOnly;

    /// <summary>The buttons show text only.</summary>
    public bool IsTextOnly => _appearance.TextPosition == ToolbarTextPosition.TextOnly;

    /// <summary>The buttons show the text to the right of the icon.</summary>
    public bool IsTextBesideIcons => _appearance.TextPosition == ToolbarTextPosition.TextBesideIcons;

    /// <summary>The buttons show the text under the icon.</summary>
    public bool IsTextUnderIcons => _appearance.TextPosition == ToolbarTextPosition.TextUnderIcons;

    /// <summary>The buttons show their text (every position but <see cref="IsIconsOnly"/>).</summary>
    public bool ShowsText => !IsIconsOnly;

    /// <summary>The icons are small (16 × 16).</summary>
    public bool IsSmallIcons => _appearance.IconSize == ToolbarIconSize.Small;

    /// <summary>The icons are medium (24 × 24).</summary>
    public bool IsMediumIcons => _appearance.IconSize == ToolbarIconSize.Medium;

    /// <summary>The icons are big (32 × 32).</summary>
    public bool IsBigIcons => _appearance.IconSize == ToolbarIconSize.Big;

    /// <summary>The toolbars cannot be moved: their grips are hidden.</summary>
    public bool ArePositionsLocked => _appearance.PositionsLocked;

    /// <summary>The button texts have a font family of their own (otherwise the interface font).</summary>
    public bool HasCustomFontFamily => _appearance.FontFamily.Length > 0;

    /// <summary>The button texts have a size of their own (otherwise the size of the buttons).</summary>
    public bool HasCustomFontSize => _appearance.FontSize > 0;

    /// <summary>The button texts have a weight of their own.</summary>
    public bool HasCustomFontWeight => _appearance.FontWeight > 0;

    /// <summary>Re-reads the shared look after <see cref="ToolbarManager.AppearanceChanged"/>.</summary>
    public void RefreshAppearance()
    {
        _appearance = _manager.Appearance;
        foreach (string name in AppearanceProperties)
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>Text Position › … — for all toolbars.</summary>
    [RelayCommand]
    private void SetTextPosition(ToolbarTextPosition position) => _manager.SetTextPosition(position);

    /// <summary>Icon Size › … — for all toolbars.</summary>
    [RelayCommand]
    private void SetIconSize(ToolbarIconSize size) => _manager.SetIconSize(size);

    /// <summary>Lock Toolbar Positions — for all toolbars.</summary>
    [RelayCommand]
    private void TogglePositionsLocked() => _manager.SetPositionsLocked(!_manager.Appearance.PositionsLocked);

    private static readonly string[] AppearanceProperties =
    {
        nameof(IsIconsOnly), nameof(IsTextOnly), nameof(IsTextBesideIcons), nameof(IsTextUnderIcons), nameof(ShowsText),
        nameof(IsSmallIcons), nameof(IsMediumIcons), nameof(IsBigIcons), nameof(ArePositionsLocked),
        nameof(HasCustomFontFamily), nameof(HasCustomFontSize), nameof(HasCustomFontWeight),
    };

    // Actions of the heading and case menu buttons, in display order.
    private static readonly string[] HeadingActionIds =
    {
        AppActionIds.Heading1, AppActionIds.Heading2, AppActionIds.Heading3, AppActionIds.Heading4,
        AppActionIds.Heading5, AppActionIds.Heading6, AppActionIds.HeadingNormal,
    };

    private static readonly string[] CaseActionIds =
    {
        AppActionIds.CasingLowercase, AppActionIds.CasingUppercase, AppActionIds.CasingTitlecase,
        AppActionIds.CasingCapitalize,
    };

    private List<AppAction> Resolve(IEnumerable<string> ids) =>
        ids.Select(_actions.Get).OfType<AppAction>().ToList();
}

/// <summary>
/// A row of toolbars (the area between two toolbar breaks). A row without any visible toolbar
/// is collapsed.
/// </summary>
public sealed class ToolbarRowViewModel : ObservableObject, System.IDisposable
{
    /// <summary>Creates a row from the given toolbars (in display order).</summary>
    public ToolbarRowViewModel(IReadOnlyList<ToolbarViewModel> toolbars)
    {
        Toolbars = toolbars;
        foreach (ToolbarViewModel toolbar in toolbars)
        {
            toolbar.PropertyChanged += OnToolbarPropertyChanged;
        }
    }

    /// <summary>Toolbars of the row.</summary>
    public IReadOnlyList<ToolbarViewModel> Toolbars { get; }

    /// <summary>Whether the row has at least one visible toolbar.</summary>
    public bool HasVisibleToolbars => Toolbars.Any(t => t.IsVisible);

    /// <summary>Stops listening to the toolbars (the toolbars outlive the rows built over them).</summary>
    public void Dispose()
    {
        foreach (ToolbarViewModel toolbar in Toolbars)
        {
            toolbar.PropertyChanged -= OnToolbarPropertyChanged;
        }
    }

    private void OnToolbarPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolbarViewModel.IsVisible))
        {
            OnPropertyChanged(nameof(HasVisibleToolbars));
        }
    }
}

/// <summary>
/// An area of the main window that holds toolbar rows (<see cref="ToolbarArea"/>). Follows the placement kept by
/// <see cref="ToolbarManager"/> and applies the moves made by dragging toolbars.
/// </summary>
public sealed class ToolbarAreaViewModel : ObservableObject
{
    private readonly ToolbarManager _manager;
    private readonly IReadOnlyList<ToolbarViewModel> _toolbars;

    /// <summary>Creates the area over the shared toolbar view models.</summary>
    public ToolbarAreaViewModel(ToolbarArea area, ToolbarManager manager, IReadOnlyList<ToolbarViewModel> toolbars)
    {
        Area = area;
        _manager = manager;
        _toolbars = toolbars;
        Rebuild();
        manager.PlacementChanged += (_, _) => Rebuild();
        manager.AllHiddenChanged += (_, _) => OnPropertyChanged(nameof(HasVisibleToolbars));
    }

    /// <summary>Which area this is.</summary>
    public ToolbarArea Area { get; }

    /// <summary>Rows of the area, top to bottom (their order matches <see cref="ToolbarManager.GetRows"/>).</summary>
    public ObservableCollection<ToolbarRowViewModel> Rows { get; } = new();

    /// <summary>Whether at least one toolbar of the area is shown (none while all are hidden, <see cref="ToolbarManager.AllHidden"/>).</summary>
    public bool HasVisibleToolbars => !_manager.AllHidden && Rows.Any(r => r.HasVisibleToolbars);

    /// <summary>
    /// Moves a dragged toolbar to the place found by <see cref="ToolbarDropResolver"/>: into a new row, or into an
    /// existing row before another toolbar.
    /// </summary>
    public void Drop(ToolbarId toolbar, ToolbarDropTarget target)
    {
        if (target.NewRow)
        {
            _manager.MoveToolbarToNewRow(toolbar, Area, target.RowIndex);
        }
        else
        {
            _manager.MoveToolbar(toolbar, Area, target.RowIndex, target.Before);
        }
    }

    private void Rebuild()
    {
        foreach (ToolbarRowViewModel row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
            row.Dispose();
        }

        Rows.Clear();
        foreach (IReadOnlyList<ToolbarId> ids in _manager.GetRows(Area))
        {
            ToolbarRowViewModel row = new(ids.Select(id => _toolbars.First(t => t.Id == id)).ToList());
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasVisibleToolbars));
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolbarRowViewModel.HasVisibleToolbars))
        {
            OnPropertyChanged(nameof(HasVisibleToolbars));
        }
    }
}
