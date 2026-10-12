using Avalonia.Controls;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// A "Fonts" page of the Preferences window (<see cref="FontPageViewModel"/>): a row per font setting and the preview of
/// the setting focused or changed last.
/// </summary>
public partial class FontsPageView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public FontsPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildRows();
    }

    // The rows are created here rather than by an ItemsControl: the controls of a page exist before the page is shown
    // (an ItemsControl realizes its items only then), so the search of the Preferences window finds their labels.
    private void BuildRows()
    {
        Rows.Children.Clear();
        if (DataContext is not FontPageViewModel page)
        {
            return;
        }

        foreach (FontSettingViewModel font in page.Fonts)
        {
            FontSettingView row = new() { DataContext = font, Name = "Font" + font.Key };
            row.AddHandler(GotFocusEvent, (_, _) => page.Active = font);
            Rows.Children.Add(row);
        }
    }
}
