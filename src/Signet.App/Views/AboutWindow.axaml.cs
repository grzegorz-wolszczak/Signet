using Avalonia.Controls;
using Signet.App.Infrastructure;

namespace Signet.App.Views;

/// <summary>The "About" window (Help → About) — the name and version of Signet (<see cref="AppVersion"/>).</summary>
public partial class AboutWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = AppVersion.Text;
        OkButton.Click += (_, _) => Close();
    }
}
