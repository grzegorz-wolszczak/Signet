using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>The start screen in the empty document area while no book is open (see <c>StartScreenViewModel</c>).</summary>
public partial class StartScreenView : UserControl
{
    private StartScreenViewModel? _boundViewModel;

    // The styles of the look of the list (Preferences › Appearance › Start Screen), replaced when it changes.
    private Style[] _appearanceStyles = Array.Empty<Style>();

    /// <summary>Initializes the view.</summary>
    public StartScreenView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) =>
        {
            AppTheme.Changed += OnAppThemeChanged;
            ApplyAppearance();
        };
        DetachedFromVisualTree += (_, _) => AppTheme.Changed -= OnAppThemeChanged;
    }

    private void OnAppThemeChanged(object? sender, EventArgs e) => ApplyAppearance();

    private void OnAppearanceChanged(object? sender, EventArgs e) => ApplyAppearance();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.AppearanceChanged -= OnAppearanceChanged;
        }

        _boundViewModel = DataContext as StartScreenViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.AppearanceChanged += OnAppearanceChanged;
        }

        ApplyAppearance();
    }

    /// <summary>
    /// Applies the look of the list of recent files: the fonts of the name, the folder and the heading, the indentation of the
    /// entries and the colors of the theme (the name, the folder, the entry under the pointer). What is not set keeps the
    /// default of the XAML. The styles of the previous look are replaced.
    /// </summary>
    private void ApplyAppearance()
    {
        foreach (Style old in _appearanceStyles)
        {
            Styles.Remove(old);
        }

        if (_boundViewModel is null)
        {
            _appearanceStyles = Array.Empty<Style>();
            return;
        }

        StartScreenAppearance look = _boundViewModel.Appearance;
        ResolvedTheme theme = AppTheme.Current;

        Style title = new(x => x.OfType<TextBlock>().Class("recentTitle"));
        AddFont(title, look.TitleFontFamily, look.TitleFontSize, look.TitleFontWeight);
        if (theme.Color(ThemeKeys.UiStartScreenTitle) is { } titleColor)
        {
            title.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(titleColor)));
        }

        Style location = new(x => x.OfType<TextBlock>().Class("recentLocation"));
        AddFont(location, look.LocationFontFamily, look.LocationFontSize, look.LocationFontWeight);
        if (theme.Color(ThemeKeys.UiStartScreenLocation) is { } locationColor)
        {
            // A color of the theme is the color shown: no dimming on top of it.
            location.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(locationColor)));
            location.Setters.Add(new Setter(OpacityProperty, 1.0));
        }

        Style section = new(x => x.OfType<TextBlock>().Class("recentSection"));
        AddFont(section, look.SectionFontFamily, look.SectionFontSize, look.SectionFontWeight);

        Style entry = new(x => x.OfType<Button>().Class("recentFile"));
        entry.Setters.Add(new Setter(PaddingProperty, new Thickness(look.Indent, 4, 8, 4)));

        System.Collections.Generic.List<Style> styles = new() { title, location, section, entry };
        if (theme.Color(ThemeKeys.UiStartScreenHighlight) is { } highlightColor)
        {
            // The entry under the pointer, and the one the sample of Preferences draws as if it were.
            SolidColorBrush highlight = new(highlightColor);
            foreach (Style hover in new[]
                     {
                         new Style(x => x.OfType<Button>().Class("recentFile").Class(":pointerover")
                             .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
                         new Style(x => x.OfType<Button>().Class("recentFile").Class("highlighted")
                             .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")),
                     })
            {
                hover.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, highlight));
                styles.Add(hover);
            }
        }

        _appearanceStyles = styles.ToArray();
        foreach (Style style in _appearanceStyles)
        {
            Styles.Add(style);
        }
    }

    // A weight that is not set keeps the weight of the XAML (the heading is semi-bold, the rest regular).
    private static void AddFont(Style style, string family, int size, int weight)
    {
        if (weight > 0)
        {
            style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeightOptions.ToAvalonia(weight)));
        }

        if (family.Length > 0)
        {
            style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily(family)));
        }

        if (size > 0)
        {
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, (double)size));
        }
    }
}
