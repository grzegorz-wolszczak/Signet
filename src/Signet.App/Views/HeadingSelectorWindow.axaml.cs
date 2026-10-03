using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The modal "Generate Table Of Contents" dialog — choosing the headings for the table of contents.
/// </summary>
public partial class HeadingSelectorWindow : Window
{
    private HeadingSelectorViewModel? _bound;

    /// <summary>Initializes the window.</summary>
    public HeadingSelectorWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Shows the dialog modally and returns <c>true</c> when the user accepted ("OK").
    /// </summary>
    public static async Task<bool> RunAsync(Window owner, HeadingSelectorViewModel viewModel)
    {
        var window = new HeadingSelectorWindow { DataContext = viewModel };
        await window.ShowDialog(owner);
        return viewModel.Accepted;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.CloseRequested -= OnCloseRequested;
        }

        _bound = DataContext as HeadingSelectorViewModel;

        if (_bound is not null)
        {
            _bound.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bound is null)
        {
            return;
        }

        // Left/right arrows = change the level of the selected heading.
        if (e.Key == Key.Left && _bound.DecreaseLevelCommand.CanExecute(null))
        {
            _bound.DecreaseLevelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && _bound.IncreaseLevelCommand.CanExecute(null))
        {
            _bound.IncreaseLevelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
