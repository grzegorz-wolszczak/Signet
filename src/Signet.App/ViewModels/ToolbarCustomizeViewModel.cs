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
    public ToolbarEntry(string id, string label)
    {
        Id = id;
        Label = label;
    }

    /// <summary>Action id or <see cref="ToolbarManager.Separator"/>.</summary>
    public string Id { get; }

    /// <summary>Label to display.</summary>
    public string Label { get; }

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
            .Select(a => new ToolbarEntry(a.Id, $"{a.CategoryDisplayName}: {AppAction.ConvertMnemonics(a.DefaultText).Replace("_", string.Empty)}"))
            .Append(new ToolbarEntry(ToolbarManager.HeadingsMenu, Strings.Get("ToolbarCustomizeWindow_HeadingMenuEntry")))
            .Append(new ToolbarEntry(ToolbarManager.CaseMenu, Strings.Get("ToolbarCustomizeWindow_CaseMenuEntry")))
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

    /// <summary>The current layout of the selected toolbar.</summary>
    public ObservableCollection<ToolbarEntry> CurrentItems { get; } = new();

    /// <summary>The selected toolbar.</summary>
    [ObservableProperty]
    private ToolbarId _selectedToolbar;

    /// <summary>The action selected in the list of available ones.</summary>
    [ObservableProperty]
    private ToolbarEntry? _selectedAvailable;

    /// <summary>The item selected in the current layout.</summary>
    [ObservableProperty]
    private ToolbarEntry? _selectedCurrent;

    partial void OnSelectedToolbarChanged(ToolbarId value) => LoadCurrent();

    /// <summary>Adds the selected action to the end of the toolbar.</summary>
    [RelayCommand]
    private void Add()
    {
        if (SelectedAvailable is { } entry)
        {
            CurrentItems.Add(entry);
            Persist();
        }
    }

    /// <summary>Adds a separator to the end of the toolbar.</summary>
    [RelayCommand]
    private void AddSeparator()
    {
        CurrentItems.Add(new ToolbarEntry(ToolbarManager.Separator, "———"));
        Persist();
    }

    /// <summary>Removes the selected item.</summary>
    [RelayCommand]
    private void Remove()
    {
        if (SelectedCurrent is { } entry)
        {
            CurrentItems.Remove(entry);
            Persist();
        }
    }

    /// <summary>Moves the selected item up.</summary>
    [RelayCommand]
    private void MoveUp()
    {
        int i = SelectedCurrent is null ? -1 : CurrentItems.IndexOf(SelectedCurrent);
        if (i > 0)
        {
            CurrentItems.Move(i, i - 1);
            Persist();
        }
    }

    /// <summary>Moves the selected item down.</summary>
    [RelayCommand]
    private void MoveDown()
    {
        int i = SelectedCurrent is null ? -1 : CurrentItems.IndexOf(SelectedCurrent);
        if (i >= 0 && i < CurrentItems.Count - 1)
        {
            CurrentItems.Move(i, i + 1);
            Persist();
        }
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
