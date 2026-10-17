using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>A tooltip drawn in the preview of the Preferences window, in the font of the setting it is bound to (see <c>TooltipSample.axaml</c>).</summary>
public partial class TooltipSample : UserControl
{
    /// <summary>Initializes the sample.</summary>
    public TooltipSample()
    {
        InitializeComponent();
    }

    /// <summary>The text of the sample, to look at its font.</summary>
    public TextBlock Text => SampleText;
}
