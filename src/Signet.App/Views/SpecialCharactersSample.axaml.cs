using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Core.MainUI;

namespace Signet.App.Views;

/// <summary>
/// The "Insert Special Character" window for the preview of the Preferences window (see <c>SpecialCharactersSample.axaml</c>):
/// the same table over a few of the curated characters, whose "Character" column takes the font of the setting that is the
/// DataContext, unsaved changes too. Nothing in it does anything.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table source lives as long as the control and observes only its own rows.")]
public partial class SpecialCharactersSample : UserControl
{
    // How many of the curated characters the sample shows.
    private const int SampleRows = 12;

    private readonly LocalizedColumns<CharRow> _columns = new();
    private FontSettingViewModel? _font;
    private Style? _symbolStyle;

    /// <summary>Initializes the sample.</summary>
    public SpecialCharactersSample()
    {
        InitializeComponent();
        CharRow[] rows = SpecialCharacters.Entries.Take(SampleRows).Select(SpecialCharacterWindow.FromEntry).ToArray();
        rows[1].IsFavorite = true;
        Results.Source = SpecialCharacterWindow.BuildSource(_columns, rows);
        DataContextChanged += (_, _) => Track(DataContext as FontSettingViewModel);
    }

    private void Track(FontSettingViewModel? font)
    {
        if (_font is not null)
        {
            _font.PropertyChanged -= OnFontChanged;
        }

        _font = font;
        if (_font is not null)
        {
            _font.PropertyChanged += OnFontChanged;
        }

        ApplySymbolFont();
    }

    private void OnFontChanged(object? sender, PropertyChangedEventArgs e) => ApplySymbolFont();

    // The characters of the "Character" column: the font of the setting (the window does the same with the saved one).
    private void ApplySymbolFont()
    {
        if (_symbolStyle is not null)
        {
            Styles.Remove(_symbolStyle);
            _symbolStyle = null;
        }

        if (_font is null)
        {
            return;
        }

        Style style = new(x => x.OfType<TextBlock>().Class("symbol"));
        if (!string.IsNullOrWhiteSpace(_font.EffectiveFamily))
        {
            style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily(_font.EffectiveFamily)));
        }

        if (_font.EffectiveSize > 0)
        {
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, _font.EffectiveSize));
        }

        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeightOptions.ToAvalonia(_font.EffectiveWeight)));
        _symbolStyle = style;
        Styles.Add(style);
    }
}
