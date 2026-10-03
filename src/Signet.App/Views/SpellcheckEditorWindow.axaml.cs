using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// Modeless "Spellcheck Editor" window. Like <see cref="ReportsWindow"/>, MainWindow keeps a single
/// window instance AND a single persistent <see cref="SpellcheckEditorViewModel"/> instance (not
/// rebuilt on every open — see the remarks in that class), so this window's <c>DataContext</c>
/// does not change.
/// </summary>
public partial class SpellcheckEditorWindow : Window
{
    private SpellcheckEditorViewModel? _bound;

    /// <summary>Initializes the window.</summary>
    public SpellcheckEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        WordsGrid.DoubleTapped += OnWordDoubleTapped;
        WordsGrid.SelectionChanged += OnSelectionChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _bound = DataContext as SpellcheckEditorViewModel;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnWordDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_bound is not null && WordsGrid.SelectedItem is SpellcheckWordRow row)
        {
            _bound.RequestNavigation(row);
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _bound?.SetSelectedWords(WordsGrid.SelectedItems.OfType<SpellcheckWordRow>());
    }
}
