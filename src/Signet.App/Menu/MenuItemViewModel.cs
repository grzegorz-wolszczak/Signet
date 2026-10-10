using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Actions;

namespace Signet.App.Menu;

/// <summary>
/// A node of the main menu tree. Three variants: a separator (<see cref="IsSeparator"/>),
/// a submenu (non-empty <see cref="Items"/>, no <see cref="Command"/>) or an action item
/// (bound to an <see cref="AppAction"/>). Avalonia renders a separator when the header is <c>-</c>.
/// </summary>
public sealed partial class MenuItemViewModel : ObservableObject
{
    /// <summary>Separator header recognized by Avalonia's <c>Menu</c> control.</summary>
    public const string SeparatorHeader = "-";

    private readonly AppAction? _action;

    private MenuItemViewModel(
        string header,
        AppAction? action,
        IReadOnlyList<MenuItemViewModel>? items,
        bool isSeparator,
        ICommand? command = null,
        bool isCheckable = false)
    {
        _header = header;
        _action = action;
        _externalCommand = command;
        Items = items;
        IsSeparator = isSeparator;
        _isCheckable = isCheckable || (action?.IsCheckable ?? false);

        if (action is not null)
        {
            _isChecked = action.IsChecked;
            action.PropertyChanged += OnActionPropertyChanged;
        }
    }

    private readonly ICommand? _externalCommand;

    /// <summary>Creates a separator.</summary>
    public static MenuItemViewModel Separator() => new(SeparatorHeader, null, null, isSeparator: true);

    /// <summary>Creates a submenu with children.</summary>
    public static MenuItemViewModel Submenu(string header, IReadOnlyList<MenuItemViewModel> items) =>
        new(header, null, items, isSeparator: false);

    /// <summary>Creates an item bound to an action (checkable when <see cref="AppAction.IsCheckable"/>).</summary>
    public static MenuItemViewModel ForAction(AppAction action) =>
        new(action.Text, action, null, isSeparator: false);

    /// <summary>Creates an item with its own command (e.g. "Customize…").</summary>
    public static MenuItemViewModel ForCommand(string header, ICommand command) =>
        new(header, null, null, isSeparator: false, command);

    /// <summary>Creates a checkable (checkbox) item with its own toggling command.</summary>
    public static MenuItemViewModel ForToggle(string header, bool isChecked, ICommand command) =>
        new(header, null, null, isSeparator: false, command, isCheckable: true) { IsChecked = isChecked };

    /// <summary>Item header (for an action it tracks <see cref="AppAction.Text"/>).</summary>
    [ObservableProperty]
    private string _header;

    /// <summary>Submenu children, or <see langword="null"/> for a leaf.</summary>
    public IReadOnlyList<MenuItemViewModel>? Items { get; }

    /// <summary>Whether this is a separator.</summary>
    public bool IsSeparator { get; }

    /// <summary>
    /// Items of the context menu shown on a right click on this item (e.g. "Remove from Recent Files"),
    /// or <see langword="null"/> when the item has none.
    /// </summary>
    public IReadOnlyList<MenuItemViewModel>? ContextItems { get; set; }

    /// <summary>Whether the item is checkable (rendered as a checkbox).</summary>
    [ObservableProperty]
    private bool _isCheckable;

    /// <summary>Check state (for <see cref="IsCheckable"/>).</summary>
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>Item command (the action or its own command), or <see langword="null"/>.</summary>
    public ICommand? Command => _action ?? _externalCommand;

    /// <summary>Action identifier or an empty string (diagnostics / tests).</summary>
    public string ActionId => _action?.Id ?? string.Empty;

    /// <summary>Shortcut text displayed on the right side of the item.</summary>
    public string InputGestureText => _action?.InputGestureText ?? string.Empty;

    /// <summary>Item keyboard shortcut (shown by <c>MenuItem</c>), or <see langword="null"/>.</summary>
    public KeyGesture? InputGesture => _action?.Gesture;

    /// <summary>
    /// Action icon key (the menu shows icons). Checkable items have no icon —
    /// Avalonia draws the check mark in its place.
    /// </summary>
    public string? IconKey => IsCheckable ? null : _action?.IconKey;

    /// <summary>Icon appearance state (e.g. the gray/red "Save" floppy disk).</summary>
    public ActionIconState IconState => _action?.IconState ?? ActionIconState.Normal;

    /// <summary>Item toggle type (checkbox for checkable items).</summary>
    public MenuItemToggleType ToggleType =>
        IsCheckable ? MenuItemToggleType.CheckBox : MenuItemToggleType.None;

    partial void OnIsCheckableChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleType));
        OnPropertyChanged(nameof(IconKey));
    }

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppAction.Text):
                Header = _action!.Text;
                break;
            case nameof(AppAction.InputGestureText):
                OnPropertyChanged(nameof(InputGestureText));
                break;
            case nameof(AppAction.Gesture):
                OnPropertyChanged(nameof(InputGesture));
                break;
            case nameof(AppAction.IsChecked):
                IsChecked = _action!.IsChecked;
                break;
            case nameof(AppAction.IsCheckable):
                IsCheckable = _action!.IsCheckable;
                break;
            case nameof(AppAction.IconState):
                OnPropertyChanged(nameof(IconState));
                break;
        }
    }
}
