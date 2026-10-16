using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>
/// The list of the themes of the interface or of the color schemes of the editors with the gear of its actions; the
/// <c>DataContext</c> is the <see cref="ViewModels.ThemeDocumentViewModel"/> it manages.
/// </summary>
public partial class ThemeSelectorView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public ThemeSelectorView()
    {
        InitializeComponent();
    }

    /// <summary>The list.</summary>
    public ComboBox List => Box;

    /// <summary>The gear.</summary>
    public Button Gear => ActionsButton;
}
