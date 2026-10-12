using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// A "Fonts" page of the Preferences window (<see cref="FontPageViewModel"/>): a row per font setting and the preview of
/// the setting focused or changed last.
/// </summary>
public partial class FontsPageView : UserControl
{
    private FontPageViewModel? _page;
    private FontSettingViewModel? _shown;

    /// <summary>Initializes the view.</summary>
    public FontsPageView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            BuildRows();
            Track(DataContext as FontPageViewModel);
        };
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

    // Follows the page and the setting it previews, to update what the bindings cannot: the code sample and wrapping.
    private void Track(FontPageViewModel? page)
    {
        if (_page is not null)
        {
            _page.PropertyChanged -= OnPageChanged;
        }

        _page = page;
        if (_page is not null)
        {
            _page.PropertyChanged += OnPageChanged;
        }

        ShowActive();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e) => ShowActive();

    private void OnActiveChanged(object? sender, PropertyChangedEventArgs e) => UpdatePreview();

    private void ShowActive()
    {
        if (_shown is not null)
        {
            _shown.PropertyChanged -= OnActiveChanged;
        }

        _shown = _page?.Active;
        if (_shown is not null)
        {
            _shown.PropertyChanged += OnActiveChanged;
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        bool wrap = _page?.WrapPreview ?? false;
        bool code = _shown?.IsCodePreview ?? false;
        PlainPreview.IsVisible = !code;
        CodePreview.IsVisible = code;
        CodePreview.WordWrap = wrap;
        PlainPreview.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PreviewText.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        if (_shown is { IsCodePreview: true, CodePreviewAppearance: { } appearance })
        {
            CodePreview.Show(_shown.PreviewText, _shown.EffectiveFamily, _shown.EffectiveSize, appearance, _shown.CodePreviewExtended);
        }
    }
}
