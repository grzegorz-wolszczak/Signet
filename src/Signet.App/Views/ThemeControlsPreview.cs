using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.Themes;

namespace Signet.App.Views;

/// <summary>
/// The preview of the pages of the theme editor without an editor: Preview (a page of a book with the highlighted
/// element) and Interface (the controls, messages and icons in the colors of Signet). Every element shows the look of
/// one key (its <see cref="Control.Tag"/>): a click reports the key (<see cref="KeyClicked"/>), and
/// <see cref="Blink"/> makes the elements of a key blink. The Fluent palette has no elements of its own.
/// </summary>
public sealed class ThemeControlsPreview : Border
{
    private const int BlinkTicks = 6;

    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly List<Control> _blinking = new();
    private ResolvedTheme _theme = ResolvedTheme.Defaults;
    private ThemePage _page = ThemePage.Interface;
    private ThemeKey? _blinkKey;
    private int _blinkTick;

    /// <summary>Creates the preview.</summary>
    public ThemeControlsPreview()
    {
        Padding = new Thickness(12);
        AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _blinkTimer.Tick += (_, _) => OnBlinkTick();
    }

    /// <summary>Raised by a click on an element shown in the look of a key.</summary>
    public event EventHandler<ThemeKey>? KeyClicked;

    /// <summary>Shows the sample of a page (<see cref="ThemePage.Preview"/> or <see cref="ThemePage.Interface"/>).</summary>
    public void Show(ThemePage page, ResolvedTheme theme)
    {
        _page = page;
        SetTheme(theme);
    }

    /// <summary>Changes the colors (the sample is built again).</summary>
    public void SetTheme(ResolvedTheme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        Child = _page == ThemePage.Preview ? BuildBook() : BuildInterface();
        Blink(_blinkTimer.IsEnabled ? _blinkKey : null);
    }

    /// <summary>Makes the elements of the key blink a few times.</summary>
    public void Blink(ThemeKey? key)
    {
        foreach (Control control in _blinking)
        {
            control.Opacity = 1;
        }

        _blinkTimer.Stop();
        _blinking.Clear();
        _blinkKey = key;
        _blinkTick = 0;
        if (key is null || Child is null)
        {
            return;
        }

        _blinking.AddRange(Child.GetVisualDescendants().OfType<Control>().Where(c => Equals(c.Tag, key)));
        if (Equals(Child.Tag, key))
        {
            _blinking.Add(Child);
        }

        if (_blinking.Count > 0)
        {
            _blinkTimer.Start();
        }
    }

    private void OnBlinkTick()
    {
        _blinkTick++;
        foreach (Control control in _blinking)
        {
            control.Opacity = _blinkTick % 2 == 1 && _blinkTick < BlinkTicks ? 0.25 : 1;
        }

        if (_blinkTick >= BlinkTicks)
        {
            _blinkTimer.Stop();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        for (Visual? visual = e.Source as Visual; visual is not null && visual != this; visual = visual.GetVisualParent())
        {
            if (visual is Control { Tag: ThemeKey key })
            {
                e.Handled = true;
                KeyClicked?.Invoke(this, key);
                return;
            }
        }
    }

    // ---- Preview: a page of a book ----

    private Border BuildBook()
    {
        IBrush text = Brush(_theme.Color(ThemeKeys.PreviewText)) ?? (IBrush)Brushes.Black;
        TextBlock Paragraph(string content, double size = 14, FontWeight weight = FontWeight.Normal) =>
            new() { Text = content, Foreground = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, Tag = ThemeKeys.PreviewText };

        Color highlight = _theme.Color(ThemeKeys.PreviewHighlight) ?? Colors.Yellow;
        Border highlighted = new()
        {
            Tag = ThemeKeys.PreviewHighlight,
            Background = new SolidColorBrush(highlight, 0.45),
            BorderBrush = new SolidColorBrush(highlight),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2),
            Child = Paragraph(Strings.Get("ColorSchemeSample_BookHighlight")),
        };
        return new Border
        {
            Tag = ThemeKeys.PreviewBackground,
            Background = Brush(_theme.Color(ThemeKeys.PreviewBackground)) ?? (IBrush)Brushes.White,
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Paragraph(Strings.Get("ColorSchemeSample_BookTitle"), 20, FontWeight.Bold),
                    Paragraph(Strings.Get("ColorSchemeSample_BookText")),
                    highlighted,
                    Paragraph(Strings.Get("ColorSchemeSample_BookNote")),
                },
            },
        };
    }

    // ---- Interface: the controls, messages and icons ----

    private StackPanel BuildInterface()
    {
        WrapPanel controls = new() { Orientation = Orientation.Horizontal };
        controls.Children.Add(Spaced(new Border
        {
            Tag = ThemeKeys.UiAccent,
            Background = Brush(_theme.Color(ThemeKeys.UiAccent)) ?? AccentFallback(),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 5),
            Child = new TextBlock { Text = Strings.Get("Common_Ok"), Foreground = Brushes.White },
        }));
        TextAttributes selection = _theme.Attributes(ThemeKeys.UiTextSelection);
        controls.Children.Add(Spaced(new Border
        {
            Tag = ThemeKeys.UiTextSelection,
            Background = Brush(selection.Background) ?? (IBrush)Brushes.LightBlue,
            Padding = new Thickness(2, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Strings.Get("ColorSchemeSample_SelectedText"), Foreground = Brush(selection.Foreground) ?? (IBrush)Brushes.Black },
        }));
        IBrush dangerText = Brush(_theme.Color(ThemeKeys.UiDangerButtonText)) ?? (IBrush)Brushes.White;
        foreach (ThemeKey key in new[] { ThemeKeys.UiDangerButton, ThemeKeys.UiDangerButtonHover, ThemeKeys.UiDangerButtonPressed })
        {
            controls.Children.Add(Spaced(new Border
            {
                Tag = key,
                Background = Brush(_theme.Color(key)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 5),
                Child = new TextBlock { Text = Strings.Get("ColorScheme_Delete"), Foreground = dangerText, Tag = ThemeKeys.UiDangerButtonText },
            }));
        }

        controls.Children.Add(Spaced(new Border
        {
            Tag = ThemeKeys.UiFrame,
            BorderBrush = Brush(_theme.Color(ThemeKeys.UiFrame)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4),
            Child = new TextBlock { Text = Strings.Get("ColorSchemeSample_Frame") },
        }));

        WrapPanel messages = new() { Orientation = Orientation.Horizontal };
        foreach ((ThemeKey key, string text) in new[]
        {
            (ThemeKeys.UiMutedText, Strings.Get("ColorSchemeSample_MutedText")),
            (ThemeKeys.UiWarningText, Strings.Get("ColorSchemeSample_Warning")),
            (ThemeKeys.UiErrorNotification, Strings.Get("ColorSchemeSample_ErrorNotification")),
            (ThemeKeys.UiErrorText, Strings.Get("ColorSchemeSample_Error")),
            (ThemeKeys.UiRenameErrorText, Strings.Get("ColorSchemeSample_RenameError")),
            (ThemeKeys.UiRiskMarker, "⚠"),
        })
        {
            messages.Children.Add(Spaced(new TextBlock { Text = text, Foreground = Brush(_theme.Color(key)), Tag = key }));
        }

        messages.Children.Add(Spaced(new Ellipse
        {
            Width = 8, Height = 8, Fill = Brush(_theme.Color(ThemeKeys.UiFilterActive)), Tag = ThemeKeys.UiFilterActive,
            VerticalAlignment = VerticalAlignment.Center,
        }));

        WrapPanel icons = new() { Orientation = Orientation.Horizontal };
        foreach (ThemeKey key in new[] { ThemeKeys.UiIconInactive, ThemeKeys.UiIconAttention })
        {
            icons.Children.Add(Spaced(new PathIcon
            {
                Data = Application.Current?.FindResource("SignetBellGeometry") as Geometry,
                Foreground = Brush(_theme.Color(key)), Width = 16, Height = 16, Tag = key,
            }));
        }

        icons.Children.Add(Spaced(Icon(
            Part("M 1 3 H 6 L 7.5 4.5 H 15 V 13.5 H 1 Z", ThemeKeys.UiIconFolder),
            Part("M 1.75 6 H 14.25 V 12.75 H 1.75 Z", ThemeKeys.UiIconFolderInner))));
        icons.Children.Add(Spaced(Icon(
            Part("M 3 1.5 H 9.5 L 13 5 V 14.5 H 3 Z", ThemeKeys.UiIconFile),
            Part("M 9.5 1.5 V 5 H 13 Z", ThemeKeys.UiIconFileFold),
            Outline("M 3 1.5 H 9.5 L 13 5 V 14.5 H 3 Z M 9.5 1.5 V 5 H 13", ThemeKeys.UiIconFileOutline))));
        TextAttributes fontPreview = _theme.Attributes(ThemeKeys.UiFontPreview);
        icons.Children.Add(Spaced(new Border
        {
            Tag = ThemeKeys.UiFontPreview,
            Background = Brush(fontPreview.Background) ?? (IBrush)Brushes.White,
            Padding = new Thickness(8, 2),
            Child = new TextBlock { Text = "Aa Bb Cc", FontSize = 16, Foreground = Brush(fontPreview.Foreground) ?? (IBrush)Brushes.Black },
        }));

        return new StackPanel { Spacing = 10, Children = { controls, messages, icons } };
    }

    private static Control Spaced(Control control)
    {
        control.Margin = new Thickness(0, 0, 12, 0);
        return control;
    }

    private Path Part(string data, ThemeKey key) => new() { Data = Geometry.Parse(data), Fill = Brush(_theme.Color(key)), Tag = key };

    private Path Outline(string data, ThemeKey key) =>
        new() { Data = Geometry.Parse(data), Stroke = Brush(_theme.Color(key)), StrokeThickness = 1, Tag = key };

    // An icon of 16×16 units drawn at twice the size.
    private static Viewbox Icon(params Path[] parts)
    {
        Canvas canvas = new() { Width = 16, Height = 16 };
        foreach (Path part in parts)
        {
            canvas.Children.Add(part);
        }

        return new Viewbox { Width = 32, Height = 32, Child = canvas };
    }

    private static IBrush AccentFallback() =>
        Application.Current?.FindResource("SystemAccentColor") is Color accent ? new SolidColorBrush(accent) : Brushes.SteelBlue;

    private static SolidColorBrush? Brush(Color? color) => color is { } c ? new SolidColorBrush(c) : null;

    private static SolidColorBrush? Brush(ThemeColor? color) => color is { } c ? new SolidColorBrush(ResolvedTheme.ToColor(c)) : null;
}
