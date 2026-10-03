using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Signet.App.Actions;

namespace Signet.App.Infrastructure;

/// <summary>
/// Action icons honoring <see cref="ActionIconState"/> — for the toolbar and the main
/// menu. The built-in icon sets are vector <see cref="DrawingImage"/>s: the
/// <see cref="ActionIconState.Inactive"/> state recolors them to shades of gray, and
/// <see cref="ActionIconState.Attention"/> to shades of red (lightness and shading are kept,
/// white/gray elements are unchanged). The "Custom" set (single-color geometry via
/// <c>PathIcon</c>) only gets the foreground color from <see cref="ForegroundForState"/>.
/// </summary>
public static class ActionIcons
{
    /// <summary>Color of a "Custom" icon in the inactive state.</summary>
    public static readonly Color InactiveColor = Color.FromRgb(0xA0, 0xA0, 0xA0);

    /// <summary>Color of a "Custom" icon in the attention state.</summary>
    public static readonly Color AttentionColor = Color.FromRgb(0xD9, 0x30, 0x30);

    // Recolored variants are held weakly, keyed by the source image — changing the icon set (new
    // DrawingImage) does not leave old copies in memory.
    private static readonly ConditionalWeakTable<IImage, Dictionary<ActionIconState, IImage>> Cache = new();

    /// <summary>
    /// Multi-value converter: [icon key, <see cref="ActionIconState"/>] → <see cref="IImage"/>
    /// (or <see langword="null"/> when the key does not point to an image — e.g. the "Custom" set).
    /// </summary>
    public static readonly IMultiValueConverter ImageForState = new FuncMultiValueConverter<object?, IImage?>(values =>
    {
        string? key = null;
        ActionIconState state = ActionIconState.Normal;
        foreach (object? value in values)
        {
            switch (value)
            {
                case string s:
                    key = s;
                    break;
                case ActionIconState st:
                    state = st;
                    break;
            }
        }

        return Resolve(key, state);
    });

    /// <summary>
    /// State → foreground color for "Custom" icons (<c>PathIcon</c>); for the normal state
    /// <see cref="AvaloniaProperty.UnsetValue"/>, i.e. the theme color.
    /// </summary>
    public static readonly IValueConverter ForegroundForState = new FuncValueConverter<ActionIconState, object?>(state => state switch
    {
        ActionIconState.Inactive => new SolidColorBrush(InactiveColor),
        ActionIconState.Attention => new SolidColorBrush(AttentionColor),
        _ => AvaloniaProperty.UnsetValue,
    });

    /// <summary>Action icon for the key <paramref name="iconKey"/> in the given state.</summary>
    public static IImage? Resolve(string? iconKey, ActionIconState state)
    {
        if (IconKeyToImageConverter.Instance.Convert(iconKey, typeof(IImage), null, CultureInfo.InvariantCulture) is not IImage image)
        {
            return null;
        }

        if (state == ActionIconState.Normal || image is not DrawingImage { Drawing: { } drawing })
        {
            return image;
        }

        Dictionary<ActionIconState, IImage> variants = Cache.GetOrCreateValue(image);
        if (!variants.TryGetValue(state, out IImage? variant))
        {
            Func<Color, Color> map = state == ActionIconState.Inactive ? ToInactive : ToAttention;
            variant = new DrawingImage(Recolor(drawing, map));
            variants[state] = variant;
        }

        return variant;
    }

    /// <summary>"Inactive" gray: luminance lightened toward light gray (like a disabled icon).</summary>
    public static Color ToInactive(Color c)
    {
        double luminance = (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
        byte gray = (byte)Math.Round((luminance * 0.45) + (190 * 0.55));
        return Color.FromArgb(c.A, gray, gray, gray);
    }

    /// <summary>
    /// "Attention" red: colored pixels get a red hue while keeping their lightness
    /// (shading is kept); nearly white/gray/black elements are unchanged.
    /// </summary>
    public static Color ToAttention(Color c)
    {
        // Neutrality is judged by chroma (max − min of the channels), not by HSL saturation — which at high
        // lightness is inflated (the light gray floppy label #d1dbe0 has S ≈ 0.2).
        int chroma = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
        HslColor hsl = c.ToHsl();
        if (chroma < 40 || hsl.L < 0.08)
        {
            return c;
        }

        double lightness = Math.Clamp(hsl.L, 0.32, 0.62);
        double saturation = Math.Max(hsl.S, 0.7);
        return HslColor.ToRgb(0, saturation, lightness, c.A / 255.0);
    }

    private static Drawing Recolor(Drawing drawing, Func<Color, Color> map)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                DrawingGroup copy = new()
                {
                    Transform = group.Transform,
                    ClipGeometry = group.ClipGeometry,
                    Opacity = group.Opacity,
                    OpacityMask = group.OpacityMask,
                };
                foreach (Drawing child in group.Children)
                {
                    copy.Children.Add(Recolor(child, map));
                }

                return copy;

            case GeometryDrawing geometry:
                return new GeometryDrawing
                {
                    Geometry = geometry.Geometry,
                    Brush = RecolorBrush(geometry.Brush, map),
                    Pen = geometry.Pen is Pen pen
                        ? new Pen(RecolorBrush(pen.Brush, map), pen.Thickness, pen.DashStyle, pen.LineCap, pen.LineJoin, pen.MiterLimit)
                        : geometry.Pen,
                };

            default:
                return drawing;
        }
    }

    private static IBrush? RecolorBrush(IBrush? brush, Func<Color, Color> map) => brush switch
    {
        ISolidColorBrush solid => new SolidColorBrush(map(solid.Color), solid.Opacity),
        _ => brush,
    };
}
