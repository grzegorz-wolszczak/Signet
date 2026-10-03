using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Toolbars;

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
public sealed class ToolbarRowViewModel : ObservableObject
{
    /// <summary>Creates a row from the given toolbars (in display order).</summary>
    public ToolbarRowViewModel(IReadOnlyList<ToolbarViewModel> toolbars)
    {
        Toolbars = toolbars;
        foreach (ToolbarViewModel toolbar in toolbars)
        {
            toolbar.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ToolbarViewModel.IsVisible))
                {
                    OnPropertyChanged(nameof(HasVisibleToolbars));
                }
            };
        }
    }

    /// <summary>Toolbars of the row.</summary>
    public IReadOnlyList<ToolbarViewModel> Toolbars { get; }

    /// <summary>Whether the row has at least one visible toolbar.</summary>
    public bool HasVisibleToolbars => Toolbars.Any(t => t.IsVisible);
}
