using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Toolbars;

namespace Signet.App.ViewModels;

/// <summary>An item in the toolbar editor — an action id (with a readable label) or a separator.</summary>
public sealed class ToolbarEntry
{
    /// <summary>Creates an item.</summary>
    public ToolbarEntry(string id, string label, string? iconKey = null)
    {
        Id = id;
        Label = label;
        IconKey = iconKey;
    }

    /// <summary>Action id or <see cref="ToolbarManager.Separator"/>.</summary>
    public string Id { get; }

    /// <summary>Label to display.</summary>
    public string Label { get; }

    /// <summary>
    /// Icon key (see <see cref="Signet.App.Infrastructure.IconThemeManager"/>) — the icon the item has on the toolbar —
    /// or <see langword="null"/> when it has none (an item without an icon shows its label instead).
    /// </summary>
    public string? IconKey { get; }

    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>
/// View model of the "Customize Toolbars" window. Allows adding/removing/moving the buttons
/// of the selected toolbar and resetting the layout. Changes are saved in <see cref="ToolbarManager"/>
/// immediately (and through it in <c>SettingsStore</c>).
/// </summary>
public sealed partial class ToolbarCustomizeViewModel : ObservableObject
{
    // The icons of the menu buttons on a toolbar (ToolbarItemViewModel.ForDropDown).
    private const string HeadingsMenuIconKey = "heading-all";
    private const string CaseMenuIconKey = "case-change";

    private readonly ToolbarManager _toolbars;
    private readonly IReadOnlyList<ToolbarEntry> _allActions;

    /// <summary>Creates the view model.</summary>
    public ToolbarCustomizeViewModel(ToolbarManager toolbars, AppActionRegistry actions)
    {
        _toolbars = toolbars ?? throw new ArgumentNullException(nameof(toolbars));
        ArgumentNullException.ThrowIfNull(actions);

        _allActions = actions.Actions
            .OrderBy(a => a.CategoryDisplayName, StringComparer.CurrentCulture)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => new ToolbarEntry(a.Id, $"{a.CategoryDisplayName}: {AppAction.ConvertMnemonics(a.DefaultText).Replace("_", string.Empty)}", a.IconKey))
            .Append(new ToolbarEntry(ToolbarManager.HeadingsMenu, Strings.Get("ToolbarCustomizeWindow_HeadingMenuEntry"), HeadingsMenuIconKey))
            .Append(new ToolbarEntry(ToolbarManager.CaseMenu, Strings.Get("ToolbarCustomizeWindow_CaseMenuEntry"), CaseMenuIconKey))
            .ToList();

        AvailableActions = new ObservableCollection<ToolbarEntry>(_allActions);
        Toolbars = new ObservableCollection<ToolbarId>(ToolbarManager.AllToolbars);
        _selectedToolbar = Toolbars[0];
        LoadCurrent();
    }

    /// <summary>All toolbars to choose from.</summary>
    public ObservableCollection<ToolbarId> Toolbars { get; }

    /// <summary>Converter of a toolbar id to its display name (the toolbar selection list in the window).</summary>
    public static FuncValueConverter<ToolbarId, string> ToolbarNameConverter { get; } =
        new(ToolbarManager.DisplayName);

    /// <summary>Actions that can be added.</summary>
    public ObservableCollection<ToolbarEntry> AvailableActions { get; }

    /// <summary>Actions matching the available-actions search.</summary>
    public IEnumerable<ToolbarEntry> FilteredAvailableActions
    {
        get
        {
            string filter = SearchText.Trim();
            if (filter.Length == 0)
            {
                return AvailableActions;
            }

            return AvailableActions
                .Where(entry => entry.Label.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                .ToArray();
        }
    }

    /// <summary>Text used to filter available actions by their labels.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The current layout of the selected toolbar.</summary>
    public ObservableCollection<ToolbarEntry> CurrentItems { get; } = new();

    /// <summary>The selected toolbar.</summary>
    [ObservableProperty]
    private ToolbarId _selectedToolbar;

    /// <summary>
    /// The actions selected in the list of available ones (several with Ctrl / Shift): <see cref="AddCommand"/> adds them
    /// all. The list's own selection (<c>ListBox.SelectedItems</c>).
    /// </summary>
    public ObservableCollection<ToolbarEntry> SelectedAvailableItems { get; } = new();

    /// <summary>
    /// The items selected in the current layout (several with Ctrl / Shift): Remove, Up and Down act on them all. The
    /// list's own selection (<c>ListBox.SelectedItems</c>).
    /// </summary>
    public ObservableCollection<ToolbarEntry> SelectedCurrentItems { get; } = new();

    partial void OnSearchTextChanged(string value)
    {
        // A selected action the search hides is not added by a click on Add.
        HashSet<ToolbarEntry> shown = FilteredAvailableActions.ToHashSet();
        foreach (ToolbarEntry hidden in SelectedAvailableItems.Where(e => !shown.Contains(e)).ToList())
        {
            SelectedAvailableItems.Remove(hidden);
        }

        OnPropertyChanged(nameof(FilteredAvailableActions));
    }

    partial void OnSelectedToolbarChanged(ToolbarId value) => LoadCurrent();

    /// <summary>Clears the available-actions search.</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>
    /// Adds the selected actions, in the order of the list, to the end of the toolbar, and selects them in the current
    /// layout. Each is a new item, so the same action added twice is two items of its own.
    /// </summary>
    [RelayCommand]
    private void Add()
    {
        List<ToolbarEntry> chosen = FilteredAvailableActions.Where(SelectedAvailableItems.Contains).ToList();
        if (chosen.Count == 0)
        {
            return;
        }

        List<ToolbarEntry> added = chosen.Select(e => new ToolbarEntry(e.Id, e.Label, e.IconKey)).ToList();
        added.ForEach(CurrentItems.Add);
        Persist();
        SelectCurrent(added);
    }

    /// <summary>Adds a separator to the end of the toolbar.</summary>
    [RelayCommand]
    private void AddSeparator()
    {
        CurrentItems.Add(new ToolbarEntry(ToolbarManager.Separator, "———"));
        Persist();
    }

    /// <summary>Removes the selected items.</summary>
    [RelayCommand]
    private void Remove()
    {
        List<ToolbarEntry> removed = CurrentItems.Where(SelectedCurrentItems.Contains).ToList();
        if (removed.Count == 0)
        {
            return;
        }

        removed.ForEach(e => CurrentItems.Remove(e));
        SelectedCurrentItems.Clear();
        Persist();
    }

    /// <summary>Moves the selected items up by one place (a selected item at the top, and those right below it, stay).</summary>
    [RelayCommand]
    private void MoveUp() => MoveSelected(-1);

    /// <summary>Moves the selected items down by one place (a selected item at the bottom, and those right above it, stay).</summary>
    [RelayCommand]
    private void MoveDown() => MoveSelected(1);

    // Moves every selected item one place in `step`'s direction, nearest to that end first, so that a block moves as a
    // whole; an item blocked by the end of the list, or by a selected item that could not move, stays.
    private void MoveSelected(int step)
    {
        List<ToolbarEntry> selected = CurrentItems.Where(SelectedCurrentItems.Contains).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        if (step > 0)
        {
            selected.Reverse();
        }

        bool moved = false;
        foreach (ToolbarEntry entry in selected)
        {
            int from = CurrentItems.IndexOf(entry);
            int to = from + step;
            if (to < 0 || to >= CurrentItems.Count || SelectedCurrentItems.Contains(CurrentItems[to]))
            {
                continue;
            }

            CurrentItems.Move(from, to);
            moved = true;
        }

        if (moved)
        {
            Persist();
            SelectCurrent(selected);
        }
    }

    // Makes `entries` the selection of the current layout (the list may drop its selection while items move).
    private void SelectCurrent(IEnumerable<ToolbarEntry> entries)
    {
        List<ToolbarEntry> keep = entries.ToList();
        SelectedCurrentItems.Clear();
        keep.ForEach(SelectedCurrentItems.Add);
    }

    /// <summary>Restores the factory layout of all toolbars.</summary>
    [RelayCommand]
    private void ResetAll()
    {
        _toolbars.ResetToDefaults();
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        CurrentItems.Clear();
        foreach (string id in _toolbars.GetItems(SelectedToolbar))
        {
            ToolbarEntry entry = id == ToolbarManager.Separator
                ? new ToolbarEntry(ToolbarManager.Separator, "———")
                : _allActions.FirstOrDefault(a => a.Id == id) ?? new ToolbarEntry(id, id);
            CurrentItems.Add(entry);
        }
    }

    private void Persist() => _toolbars.SetItems(SelectedToolbar, CurrentItems.Select(e => e.Id));
}
