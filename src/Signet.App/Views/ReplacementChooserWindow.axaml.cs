using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The modal "Filter Replacements" window — a checkbox per match; "Apply" applies
/// only the checked ones.
/// </summary>
public partial class ReplacementChooserWindow : Window
{
    private ReplacementChooserViewModel? _bound;

    /// <summary>Initializes the window.</summary>
    public ReplacementChooserWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Shows the window modally for the given view model and returns the number of replacements made
    /// (0 when closed without "Apply").
    /// </summary>
    public static async Task<int> RunAsync(Window owner, ReplacementChooserViewModel viewModel)
    {
        var window = new ReplacementChooserWindow { DataContext = viewModel };
        await window.ShowDialog(owner);
        return viewModel.ReplacementCount;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.CloseRequested -= OnCloseRequested;
        }

        _bound = DataContext as ReplacementChooserViewModel;

        if (_bound is not null)
        {
            _bound.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
