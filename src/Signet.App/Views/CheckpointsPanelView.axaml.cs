using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>
/// View of the "Checkpoints" panel — a list of book states with "Compare"
/// and "Revert" buttons.
/// </summary>
public partial class CheckpointsPanelView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public CheckpointsPanelView()
    {
        InitializeComponent();
    }
}
