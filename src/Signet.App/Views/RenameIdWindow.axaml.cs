using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>The "Rename Id" window from the Code Editor context menu: the new id and a change counter.</summary>
public partial class RenameIdWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public RenameIdWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Accept();
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>Shows the window; returns the computed rename once accepted, or <c>null</c>.</summary>
    public static Task<IdRenameResult?> AskAsync(Window owner, RenameIdViewModel viewModel)
    {
        RenameIdWindow window = new() { DataContext = viewModel };
        return window.ShowDialog<IdRenameResult?>(owner);
    }

    private void Accept()
    {
        if (DataContext is RenameIdViewModel { CanAccept: true } vm)
        {
            Close(vm.Rename());
        }
    }
}
