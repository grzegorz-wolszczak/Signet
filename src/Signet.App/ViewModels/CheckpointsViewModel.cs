using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Checkpoints" panel: the list of states from <see cref="CheckpointHistory"/>,
/// the current one highlighted and selected after every change, and "Revert" and "Compare"
/// buttons.
/// </summary>
/// <remarks>
/// Double-clicking a row intentionally does nothing (an accidental revert would be too easy).
/// The buttons are disabled for the current state instead of showing an error.
/// </remarks>
public sealed partial class CheckpointsViewModel : ViewModelBase, ILanguageAware
{
    private readonly CheckpointHistory _history;
    private CheckpointItem? _selectedItem;

    /// <summary>Creates the view model for a history (the list refreshes on <see cref="CheckpointHistory.Changed"/>).</summary>
    public CheckpointsViewModel(CheckpointHistory history)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _history.Changed += (_, _) => Rebuild();
        Strings.RegisterLanguageAware(this);
        Rebuild();
    }

    /// <summary>Request to revert the book to a state (handled by the main window).</summary>
    public event EventHandler<CheckpointState>? RevertRequested;

    /// <summary>Request to compare a state with the current one (handled by the main window).</summary>
    public event EventHandler<CheckpointState>? CompareRequested;

    /// <summary>List rows — oldest state first.</summary>
    public ObservableCollection<CheckpointItem> Items { get; } = new();

    /// <summary>Selected row.</summary>
    public CheckpointItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                RevertCommand.NotifyCanExecuteChanged();
                CompareCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Row label: the current state as "Current state [was: …]", an unnamed state as
    /// "[Unnamed state]".
    /// </summary>
    public static string LabelFor(CheckpointState state, bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (isCurrent)
        {
            return string.IsNullOrEmpty(state.Message)
                ? Strings.Get("Checkpoint_CurrentState")
                : Strings.Format("Checkpoint_CurrentStateWas", state.Message);
        }

        return string.IsNullOrEmpty(state.Message) ? Strings.Get("Checkpoint_Unnamed") : state.Message;
    }

    /// <inheritdoc />
    public void OnLanguageChanged() => Rebuild();

    private bool CanActOnSelection() => SelectedItem is { IsCurrent: false };

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void Revert()
    {
        if (SelectedItem is { IsCurrent: false } item)
        {
            RevertRequested?.Invoke(this, item.State);
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void Compare()
    {
        if (SelectedItem is { IsCurrent: false } item)
        {
            CompareRequested?.Invoke(this, item.State);
        }
    }

    // After every change the current state is selected.
    private void Rebuild()
    {
        Items.Clear();
        CheckpointItem? current = null;
        for (int i = 0; i < _history.States.Count; i++)
        {
            bool isCurrent = i == _history.Position;
            CheckpointState state = _history.States[i];
            CheckpointItem item = new(state, LabelFor(state, isCurrent), isCurrent);
            Items.Add(item);
            if (isCurrent)
            {
                current = item;
            }
        }

        SelectedItem = current;
    }
}

/// <summary>Row of the "Checkpoints" panel.</summary>
/// <param name="State">State from the history.</param>
/// <param name="Label">Displayed label.</param>
/// <param name="IsCurrent">Whether this is the current state (shown in bold).</param>
public sealed record CheckpointItem(CheckpointState State, string Label, bool IsCurrent);
