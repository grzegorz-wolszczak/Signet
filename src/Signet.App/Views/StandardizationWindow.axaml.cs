using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The modal "Standardize EPUB" dialog (<see cref="StandardizationViewModel"/>): the standardization steps as sections
/// with a ℹ description and a preview of their changes. "Apply" runs the checked steps and closes the dialog; "Close"
/// closes it without changes.
/// </summary>
public partial class StandardizationWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public StandardizationWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
    }

    /// <summary>Shows the dialog and waits until it is closed; <paramref name="created"/> gets the window before it is shown.</summary>
    public static async Task ShowAsync(Window owner, StandardizationViewModel viewModel, Action<StandardizationWindow>? created = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        StandardizationWindow window = new() { DataContext = viewModel };
        created?.Invoke(window);
        void OnCloseRequested(object? sender, EventArgs e) => window.Close();
        viewModel.CloseRequested += OnCloseRequested;
        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }
    }
}
