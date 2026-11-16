using System.Threading.Tasks;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>
/// Shown at startup on Linux when WebKitGTK is not installed: the Preview cannot be displayed until it is, and the
/// dialog says which package to install.
/// </summary>
public partial class MissingWebKitDialog : Window
{
    /// <summary>Initializes the dialog.</summary>
    public MissingWebKitDialog()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close();
    }

    /// <summary>Shows the dialog over <paramref name="owner"/> and waits until it is closed.</summary>
    public static Task ShowAsync(Window owner) => new MissingWebKitDialog().ShowDialog(owner);
}
