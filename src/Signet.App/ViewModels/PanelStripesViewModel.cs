using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Resources;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// The stripes of the panels along the left and the right side of the main window (IntelliJ's tool window bars): a
/// button for every panel, grouped by its place (<see cref="PanelPlace"/>). A click opens a closed panel or closes an
/// open one (the buttons have no pressed look); the menu of a button hides it from the stripes, moves the panel to another place
/// and shows or hides the names under the icons; "More Tool Windows" (…) lists the panels whose button was hidden.
/// The buttons follow <see cref="MainDockFactory.PanelsChanged"/>.
/// </summary>
public sealed partial class PanelStripesViewModel : ObservableObject
{
    // The action that shows each panel (View menu): its icon is the panel's icon, its shortcut is in the tooltip.
    private static readonly Dictionary<string, string> ToggleActions = new(StringComparer.Ordinal)
    {
        [DockableIds.BookBrowser] = AppActionIds.ToggleBookBrowser,
        [DockableIds.Clips] = AppActionIds.ToggleClips,
        [DockableIds.Preview] = AppActionIds.TogglePreview,
        [DockableIds.TableOfContents] = AppActionIds.ToggleToc,
        [DockableIds.ValidationResults] = AppActionIds.ToggleValidationResults,
        [DockableIds.Checkpoints] = AppActionIds.ToggleCheckpoints,
        [DockableIds.Notifications] = AppActionIds.ToggleNotifications,
        [DockableIds.FindUsages] = AppActionIds.ToggleFindUsages,
    };

    private readonly MainDockFactory _factory;
    private readonly SettingsStore _settings;
    private readonly bool _isSample;
    private readonly Dictionary<string, PanelStripeButtonViewModel> _buttons = new(StringComparer.Ordinal);
    private PanelStripeAppearance _look;

    // The width while the inner edge of a stripe is dragged (Resizable), saved when the drag ends; NaN = not dragged.
    private double _draggedWidth = double.NaN;

    /// <summary>
    /// Creates the stripes over the panels of <paramref name="factory"/> (its layout already created). A
    /// <paramref name="isSample"/> (the preview of Preferences, over the draft of the settings) always shows the names and
    /// saves nothing.
    /// </summary>
    public PanelStripesViewModel(MainDockFactory factory, SettingsStore settings, AppActionRegistry actions, bool isSample = false)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentNullException.ThrowIfNull(actions);
        foreach (string id in factory.ToolIds)
        {
            AppAction? action = ToggleActions.TryGetValue(id, out string? actionId) ? actions.Get(actionId) : null;
            _buttons[id] = new PanelStripeButtonViewModel(this, id, action);
        }

        Left = new PanelStripeViewModel(this, PanelStripeSide.Left, PanelPlace.LeftTop, PanelPlace.LeftBottom, PanelPlace.BottomLeft);
        Right = new PanelStripeViewModel(this, PanelStripeSide.Right, PanelPlace.RightTop, PanelPlace.RightBottom, PanelPlace.BottomRight);
        _isSample = isSample;
        _showNames = isSample || settings.PanelStripeNamesShown;
        _look = settings.PanelStripeAppearance;
        settings.SettingChanged += OnSettingChanged;
        _factory.PanelsChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>
    /// The width of the stripes: as wide as an icon without the names; with them, by <see cref="PanelStripeWidthMode"/> —
    /// NaN (as wide as the longest name) for Dynamic, else the width set or dragged to.
    /// </summary>
    public double Width
    {
        get
        {
            if (!ShowNames)
            {
                return PanelStripeAppearance.MinWidth;
            }

            return _look.WidthMode switch
            {
                PanelStripeWidthMode.Fixed => _look.FixedWidth,
                PanelStripeWidthMode.Resizable => double.IsNaN(_draggedWidth) ? _look.ResizedWidth : _draggedWidth,
                _ => double.NaN,
            };
        }
    }

    /// <summary>Whether the inner edge of a stripe can be dragged to widen or narrow it (Resizable, with the names shown).</summary>
    public bool CanResize => ShowNames && _look.WidthMode == PanelStripeWidthMode.Resizable;

    /// <summary>The narrowest a stripe is.</summary>
    public static double MinWidth => PanelStripeAppearance.MinWidth;

    /// <summary>The inner edge of a stripe dragged by <paramref name="delta"/> px (outwards = wider); not saved yet.</summary>
    public void ResizeBy(double delta)
    {
        if (!CanResize)
        {
            return;
        }

        _draggedWidth = Math.Clamp(Width + delta, PanelStripeAppearance.MinWidth, PanelStripeAppearance.MaxWidth);
        OnPropertyChanged(nameof(Width));
    }

    /// <summary>The drag of the inner edge ended: the width is saved.</summary>
    public void EndResize()
    {
        if (double.IsNaN(_draggedWidth))
        {
            return;
        }

        int width = (int)Math.Round(_draggedWidth);
        _draggedWidth = double.NaN;
        if (!_isSample)
        {
            _settings.PanelStripeAppearance = _settings.PanelStripeAppearance with { ResizedWidth = width };
        }

        OnPropertyChanged(nameof(Width));
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (SettingChangeKeys.Is(e, SettingChangeKeys.PanelStripeAppearance)
            || SettingChangeKeys.Is(e, SettingChangeKeys.AppearanceSettingsCleared))
        {
            _look = _settings.PanelStripeAppearance;
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(CanResize));
        }
    }

    /// <summary>The stripe along the left side: Left Top, Left Bottom, and at its foot Bottom Left and "More Tool Windows".</summary>
    public PanelStripeViewModel Left { get; }

    /// <summary>The stripe along the right side: Right Top, Right Bottom, and at its foot Bottom Right.</summary>
    public PanelStripeViewModel Right { get; }

    /// <summary>Every panel, by name: "More Tool Windows" lists them (those whose button is off the stripes too).</summary>
    public ObservableCollection<PanelStripeButtonViewModel> MoreItems { get; } = new();

    /// <summary>Whether the buttons show the names of the panels under their icons ("Show Tool Window Names").</summary>
    [ObservableProperty]
    private bool _showNames;

    partial void OnShowNamesChanged(bool value)
    {
        if (!_isSample)
        {
            _settings.PanelStripeNamesShown = value;
        }

        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(CanResize));
        foreach (PanelStripeButtonViewModel button in _buttons.Values)
        {
            button.NotifyShowNames();
        }
    }

    /// <summary>The button of a panel.</summary>
    public PanelStripeButtonViewModel ButtonOf(string id) => _buttons[id];

    /// <summary>Moves a panel to <paramref name="place"/> at <paramref name="index"/> (a stripe button dropped there).</summary>
    public void Drop(string id, PanelPlace place, int index) => _factory.MoveTool(id, place, index);

    /// <summary>"Show Tool Window Names" of a button's menu.</summary>
    [RelayCommand]
    private void ToggleShowNames() => ShowNames = !ShowNames;

    internal MainDockFactory Factory => _factory;

    /// <summary>Re-reads the panels: their places and order, which ones are open, in front or off the stripes.</summary>
    public void Refresh()
    {
        foreach (PanelStripeButtonViewModel button in _buttons.Values)
        {
            button.Refresh();
        }

        foreach (PanelStripeViewModel stripe in new[] { Left, Right })
        {
            Sync(stripe.Top, ButtonsAt(stripe.TopPlace));
            Sync(stripe.Middle, ButtonsAt(stripe.MiddlePlace));
            Sync(stripe.Bottom, ButtonsAt(stripe.BottomPlace));
        }

        Sync(MoreItems, _buttons.Values.OrderBy(b => b.Title, StringComparer.CurrentCulture));
    }

    // Changes the buttons of a group only when they changed (most changes only open or close a panel): the controls of the
    // buttons stay, with their pointer state.
    private static void Sync(ObservableCollection<PanelStripeButtonViewModel> group, IEnumerable<PanelStripeButtonViewModel> buttons)
    {
        List<PanelStripeButtonViewModel> wanted = buttons.ToList();
        if (!group.SequenceEqual(wanted))
        {
            group.ReplaceWith(wanted);
        }
    }

    private IEnumerable<PanelStripeButtonViewModel> ButtonsAt(PanelPlace place) =>
        _factory.PanelsAt(place).Where(_factory.IsOnStripe).Select(id => _buttons[id]);
}

/// <summary>One stripe: three groups of buttons (the places it holds), top to bottom.</summary>
public sealed class PanelStripeViewModel
{
    internal PanelStripeViewModel(PanelStripesViewModel owner, PanelStripeSide side, PanelPlace top, PanelPlace middle, PanelPlace bottom)
    {
        Owner = owner;
        Side = side;
        TopPlace = top;
        MiddlePlace = middle;
        BottomPlace = bottom;
    }

    /// <summary>Both stripes ("More Tool Windows", the names under the icons, dropping a button on either stripe).</summary>
    public PanelStripesViewModel Owner { get; }

    /// <summary>Which side of the window.</summary>
    public PanelStripeSide Side { get; }

    /// <summary>The place of a group of this stripe (0 = top, 1 = middle, 2 = bottom).</summary>
    public PanelPlace PlaceOfGroup(int group) => group switch
    {
        0 => TopPlace,
        1 => MiddlePlace,
        _ => BottomPlace,
    };

    /// <summary>The place of the first group (at the top of the stripe).</summary>
    public PanelPlace TopPlace { get; }

    /// <summary>The place of the second group (under a line, below the first).</summary>
    public PanelPlace MiddlePlace { get; }

    /// <summary>The place of the group at the foot of the stripe (the half of the bottom row on this side).</summary>
    public PanelPlace BottomPlace { get; }

    /// <summary>The buttons of <see cref="TopPlace"/>.</summary>
    public ObservableCollection<PanelStripeButtonViewModel> Top { get; } = new();

    /// <summary>The buttons of <see cref="MiddlePlace"/>.</summary>
    public ObservableCollection<PanelStripeButtonViewModel> Middle { get; } = new();

    /// <summary>The buttons of <see cref="BottomPlace"/>.</summary>
    public ObservableCollection<PanelStripeButtonViewModel> Bottom { get; } = new();

    /// <summary>Whether this is the stripe of "More Tool Windows" (the left one, as in IntelliJ).</summary>
    public bool HoldsMoreButton => Side == PanelStripeSide.Left;
}

/// <summary>The stripe button of a panel.</summary>
public sealed partial class PanelStripeButtonViewModel : ObservableObject
{
    private readonly PanelStripesViewModel _owner;
    private readonly AppAction? _action;

    internal PanelStripeButtonViewModel(PanelStripesViewModel owner, string id, AppAction? action)
    {
        _owner = owner;
        _action = action;
        Id = id;
        MoveToItems = PanelPlaces.All.Select(place => new PanelPlaceItemViewModel(this, place)).ToList();
        if (action is not null)
        {
            action.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(AppAction.InputGestureText) or nameof(AppAction.DefaultText))
                {
                    OnPropertyChanged(nameof(ToolTip));
                }
            };
        }
    }

    /// <summary>The id of the panel (<see cref="DockableIds"/>).</summary>
    public string Id { get; }

    /// <summary>The name of the panel.</summary>
    public string Title => MainDockFactory.StripeTitle(Id);

    /// <summary>
    /// The name under the icon (when the names are shown): in two lines, split at the first space (IntelliJ's), so the
    /// stripe stays narrow.
    /// </summary>
    public string Label
    {
        get
        {
            string title = Title.Trim();
            int space = title.IndexOf(' ', StringComparison.Ordinal);
            return space < 0 ? title : title[..space] + "\n" + title[(space + 1)..];
        }
    }

    /// <summary>The icon of the panel (the icon of its View menu action).</summary>
    public string? IconKey => _action?.IconKey;

    /// <summary>The tooltip: the name of the panel and the shortcut that shows it.</summary>
    public string ToolTip => _action is { InputGestureText.Length: > 0 } a ? $"{Title} ({a.InputGestureText})" : Title;

    /// <summary>Whether the button shows the name of the panel under the icon.</summary>
    public bool ShowNames => _owner.ShowNames;

    /// <summary>The unread errors of the panel (the Notifications bell); 0 = none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttention))]
    private int _attentionCount;

    /// <summary>Whether the button shows the red dot of unread errors.</summary>
    public bool HasAttention => AttentionCount > 0;

    /// <summary>Whether the panel has the notification bell (Notifications): its red dot shows on the button.</summary>
    public bool ShowsBell => Id == DockableIds.Notifications;

    /// <summary>The place of the panel.</summary>
    public PanelPlace Place => _owner.Factory.PlaceOf(Id);

    /// <summary>The items of "Move To" (every place; the panel's own is disabled).</summary>
    public IReadOnlyList<PanelPlaceItemViewModel> MoveToItems { get; }

    /// <summary>A click: opens a closed panel (making it active) or closes an open one.</summary>
    [RelayCommand]
    private void Click() => _owner.Factory.ClickStripeButton(Id);

    /// <summary>"Hide" of the menu: closes the panel and takes its button off the stripes ("More Tool Windows" brings it back).</summary>
    [RelayCommand]
    private void HideFromStripe() => _owner.Factory.SetOnStripe(Id, false);

    /// <summary>An item of "More Tool Windows": opens the panel (bringing it to the front) and puts its button back on its stripe if it was hidden.</summary>
    [RelayCommand]
    private void Restore()
    {
        _owner.Factory.SetOnStripe(Id, true);
        _owner.Factory.FocusTool(Id);
    }

    /// <summary>"Show Tool Window Names" of the menu (for all buttons).</summary>
    public IRelayCommand ToggleShowNamesCommand => _owner.ToggleShowNamesCommand;

    internal void MoveTo(PanelPlace place) => _owner.Factory.MoveTool(Id, place, int.MaxValue);

    internal void NotifyShowNames() => OnPropertyChanged(nameof(ShowNames));

    internal void Refresh()
    {
        AttentionCount = _owner.Factory.AttentionOf(Id);
        OnPropertyChanged(nameof(Place));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(ToolTip));
        foreach (PanelPlaceItemViewModel item in MoveToItems)
        {
            item.Refresh();
        }
    }
}

/// <summary>An item of the "Move To" menu of a stripe button.</summary>
public sealed partial class PanelPlaceItemViewModel : ObservableObject
{
    private readonly PanelStripeButtonViewModel _button;

    internal PanelPlaceItemViewModel(PanelStripeButtonViewModel button, PanelPlace place)
    {
        _button = button;
        Place = place;
    }

    /// <summary>The place.</summary>
    public PanelPlace Place { get; }

    /// <summary>Its name (Left Top, …).</summary>
    public string Name => Strings.Get(PanelPlaces.NameKey(Place));

    /// <summary>Its icon (a window with that part filled).</summary>
    public string IconKey => PanelPlaces.IconKey(Place);

    /// <summary>Whether the panel is already there (the item is disabled then).</summary>
    public bool IsCurrent => _button.Place == Place;

    /// <summary>Moves the panel there (at the end of its panels).</summary>
    [RelayCommand(CanExecute = nameof(CanMove))]
    private void Move() => _button.MoveTo(Place);

    private bool CanMove() => !IsCurrent;

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(Name));
        MoveCommand.NotifyCanExecuteChanged();
    }
}
