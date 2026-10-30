using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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
/// A submenu is enabled only while one of its items is (e.g. without a book "Change Case" is grayed out with all its
/// actions); the top-level menus stay enabled.
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
        bool isCheckable = false,
        string? iconKey = null)
    {
        _header = header;
        _action = action;
        _externalCommand = command;
        _iconKey = iconKey;
        Items = items;
        IsSeparator = isSeparator;
        _isCheckable = isCheckable || (action?.IsCheckable ?? false);

        if (action is not null)
        {
            _isChecked = action.IsChecked;
            action.PropertyChanged += OnActionPropertyChanged;
        }

        if (items is not null)
        {
            if (items is INotifyCollectionChanged observable)
            {
                observable.CollectionChanged += OnItemsChanged;
            }

            WatchItems();
        }
    }

    // The items whose state the submenu follows (re-subscribed when the list changes, e.g. the recent files).
    private readonly List<MenuItemViewModel> _watched = new();
    private bool _isTopLevel;
    private bool _isEnabled = true;

    /// <summary>
    /// Whether the item can be used: always for a leaf (its <see cref="Command"/> decides), for a submenu while one of
    /// its items is enabled; a top-level menu always.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        private set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>Marks a top-level menu (File, Edit, …): it stays enabled even when all its items are disabled.</summary>
    internal void MarkTopLevel()
    {
        _isTopLevel = true;
        UpdateIsEnabled();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => WatchItems();

    private void WatchItems()
    {
        foreach (MenuItemViewModel item in _watched)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            if (item.Command is { } command)
            {
                command.CanExecuteChanged -= OnItemCanExecuteChanged;
            }
        }

        _watched.Clear();
        _watched.AddRange(Items!.Where(i => !i.IsSeparator));
        foreach (MenuItemViewModel item in _watched)
        {
            item.PropertyChanged += OnItemPropertyChanged;
            if (item.Command is { } command)
            {
                command.CanExecuteChanged += OnItemCanExecuteChanged;
            }
        }

        UpdateIsEnabled();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsEnabled))
        {
            UpdateIsEnabled();
        }
    }

    private void OnItemCanExecuteChanged(object? sender, EventArgs e) => UpdateIsEnabled();

    private void UpdateIsEnabled()
    {
        if (Items is not null)
        {
            IsEnabled = _isTopLevel || _watched.Any(item => item.IsEnabled && (item.Command?.CanExecute(null) ?? true));
        }
    }

    private readonly ICommand? _externalCommand;
    private readonly string? _iconKey;

    /// <summary>Creates a separator.</summary>
    public static MenuItemViewModel Separator() => new(SeparatorHeader, null, null, isSeparator: true);

    /// <summary>Creates a submenu with children.</summary>
    public static MenuItemViewModel Submenu(string header, IReadOnlyList<MenuItemViewModel> items) =>
        new(header, null, items, isSeparator: false);

    /// <summary>Creates an item bound to an action (checkable when <see cref="AppAction.IsCheckable"/>).</summary>
    public static MenuItemViewModel ForAction(AppAction action) =>
        new(action.Text, action, null, isSeparator: false);

    /// <summary>Creates an item with its own command (e.g. "Customize…"), optionally with an icon.</summary>
    public static MenuItemViewModel ForCommand(string header, ICommand command, string? iconKey = null) =>
        new(header, null, null, isSeparator: false, command: command, iconKey: iconKey);

    /// <summary>
    /// Creates a checkable (checkbox) item with its own toggling command; with <paramref name="staysOpenOnClick"/> the menu
    /// stays open after a click (<see cref="StaysOpenOnClick"/>).
    /// </summary>
    public static MenuItemViewModel ForToggle(string header, bool isChecked, ICommand command, bool staysOpenOnClick = false) =>
        new(header, null, null, isSeparator: false, command, isCheckable: true) { IsChecked = isChecked, StaysOpenOnClick = staysOpenOnClick };

    /// <summary>Item header (for an action it tracks <see cref="AppAction.Text"/>).</summary>
    [ObservableProperty]
    private string _header;

    /// <summary>Submenu children, or <see langword="null"/> for a leaf.</summary>
    public IReadOnlyList<MenuItemViewModel>? Items { get; }

    /// <summary>Whether this is a separator.</summary>
    public bool IsSeparator { get; }

    /// <summary>
    /// Items of the context menu shown on a right click on this item (e.g. "Open File Location" or "Remove from Recent Files"),
    /// or <see langword="null"/> when the item has none.
    /// </summary>
    public IReadOnlyList<MenuItemViewModel>? ContextItems { get; set; }

    /// <summary>The tooltip of the item (e.g. the full path of a recent file), or <see langword="null"/> for none.</summary>
    public string? ToolTip { get; set; }

    /// <summary>
    /// Whether the item is drawn dimmed while staying clickable — a recent file that is no longer on disk. It cannot
    /// be <see cref="IsEnabled"/> = <see langword="false"/> instead: a disabled item takes neither the click (which
    /// offers to remove it from the list) nor the pointer (which shows the tooltip saying why it cannot be opened).
    /// </summary>
    public bool IsDimmed { get; set; }

    /// <summary>The opacity the item is drawn with (<see cref="IsDimmed"/>).</summary>
    public double Opacity => IsDimmed ? 0.5 : 1.0;

    /// <summary>
    /// Whether the menu stays open after the item is clicked (e.g. the toolbar toggles of View › Toolbars, so that several
    /// can be switched in a row); it closes when the pointer leaves it or another item is clicked.
    /// </summary>
    public bool StaysOpenOnClick { get; init; }

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
    /// Icon key (the menu shows icons). Checkable items have no icon —
    /// Avalonia draws the check mark in its place.
    /// </summary>
    public string? IconKey => IsCheckable ? null : _action?.IconKey ?? _iconKey;

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
