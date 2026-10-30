using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Controls.Templates;
using Signet.App.Infrastructure;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>A tab of <see cref="DocumentTabsSample"/>.</summary>
/// <param name="Caption">The text of the tab.</param>
/// <param name="IsSelected">Whether it is the tab of the document in front.</param>
public sealed record DocumentTabSampleItem(string Caption, bool IsSelected);

/// <summary>
/// A strip of document tabs for the preview of the Code Editor pages of Preferences (see <c>DocumentTabsSample.axaml</c>): its
/// DataContext is the font setting of the tabs, so the tabs take the unsaved font at once.
/// </summary>
public partial class DocumentTabsSample : UserControl
{
    /// <summary>Whether the tabs that do not fit go to several rows (otherwise they stay in one row that scrolls).</summary>
    public static readonly StyledProperty<bool> IsMultilineProperty =
        AvaloniaProperty.Register<DocumentTabsSample, bool>(nameof(IsMultiline));

    private static readonly DocumentTabSampleItem[] Items =
    {
        new("Chapter01.xhtml", true),
        new("Stylesheet.css", false),
        new("Titlepage.xhtml", false),
        new("Chapter02.xhtml", false),
        new("Figure01.svg", false),
        new("Chapter03.xhtml", false),
        new("content.opf", false),
    };

    /// <summary>Initializes the strip.</summary>
    public DocumentTabsSample()
    {
        InitializeComponent();
        Tabs.ItemsSource = Items;
        ApplyColors();
        AttachedToVisualTree += (_, _) =>
        {
            AppTheme.Changed += OnThemeChanged;
            ApplyColors();
        };
        DetachedFromVisualTree += (_, _) => AppTheme.Changed -= OnThemeChanged;
    }

    /// <summary>Gets or sets <see cref="IsMultilineProperty"/>.</summary>
    public bool IsMultiline
    {
        get => GetValue(IsMultilineProperty);
        set => SetValue(IsMultilineProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsMultilineProperty)
        {
            bool multiline = IsMultiline;
            Tabs.ItemsPanel = multiline
                ? new FuncTemplate<Panel?>(() => new WrapPanel())
                : new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal });
            Scroller.HorizontalScrollBarVisibility = multiline ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyColors();

    // The colors of the tabs: the theme's (Interface Colors) when it sets them, otherwise a look of the controls' own colors.
    private void ApplyColors()
    {
        ResolvedTheme theme = AppTheme.Current;
        Resources["SampleTabForeground"] = Brush(theme.Foreground(ThemeKeys.UiDocumentTab), FluentResourceKeys.SystemControlForegroundBaseMediumBrush);
        Resources["SampleTabBackground"] = Brush(theme.Background(ThemeKeys.UiDocumentTab), null);
        Resources["SampleTabSelectedForeground"] =
            Brush(theme.Foreground(ThemeKeys.UiDocumentTabSelected), FluentResourceKeys.SystemControlForegroundBaseHighBrush);
        Resources["SampleTabSelectedBackground"] =
            Brush(theme.Background(ThemeKeys.UiDocumentTabSelected), FluentResourceKeys.SystemControlBackgroundAltHighBrush);
    }

    private IBrush? Brush(Color? color, string? fallbackResource)
    {
        if (color is { } themeColor)
        {
            return new SolidColorBrush(themeColor);
        }

        return fallbackResource is not null && this.TryFindResource(fallbackResource, ActualThemeVariant, out object? found)
            ? found as IBrush
            : Brushes.Transparent;
    }
}
