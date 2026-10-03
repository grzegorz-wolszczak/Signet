using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>
/// The frame of the "Find &amp; Replace" panel (close button, Esc) embedded below the document
/// tab area — the panel sits below the editor, not below the whole window.
/// The data context is <see cref="ViewModels.MainWindowViewModel"/>.
/// </summary>
public partial class FindReplacePanel : UserControl
{
    /// <summary>Initializes the view.</summary>
    public FindReplacePanel()
    {
        InitializeComponent();
    }
}
