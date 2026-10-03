using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>
/// The "Rename Class" window from the Code View context menu: the new
/// class name, the scope (the whole book / the same nesting) and a change counter.
/// </summary>
public partial class RenameClassWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public RenameClassWindow()
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
    public static Task<ClassRenameResult?> AskAsync(Window owner, RenameClassViewModel viewModel)
    {
        RenameClassWindow window = new() { DataContext = viewModel };
        return window.ShowDialog<ClassRenameResult?>(owner);
    }

    private void Accept()
    {
        if (DataContext is RenameClassViewModel { CanAccept: true } vm)
        {
            Close(vm.Rename());
        }
    }
}
