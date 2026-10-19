using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// The tooltip font of each area of the application (Preferences: <see cref="TooltipFont"/> per
/// <see cref="TooltipArea"/>). <see cref="AreaProperty"/> marks the root of an area (a window or the view of a panel):
/// it gets the area's typeface and size as resources — <see cref="FamilyResourceKey"/>, which the <see cref="ToolTip"/>
/// style of <c>App.axaml</c> uses, and the Fluent <see cref="SizeResourceKey"/>. A tooltip looks its resources up through
/// the control it belongs to, so the innermost area wins. The tooltips outside every area (dialogs without one,
/// floating panels) get the font of <see cref="TooltipArea.OtherWindows"/>, set on the application.
/// <para>Only resources change: adding and removing styles at run time restyles the whole tree, which Avalonia does not
/// survive in every template (a template binding of a brush got the unset value).</para>
/// </summary>
public static class TooltipFonts
{
    /// <summary>The Fluent resource of the tooltip text size.</summary>
    public const string SizeResourceKey = "ToolTipContentThemeFontSize";

    /// <summary>The resource of the tooltip typeface (the <see cref="ToolTip"/> style of <c>App.axaml</c>).</summary>
    public const string FamilyResourceKey = "SignetToolTipFontFamily";

    /// <summary>The resource of the tooltip weight (the <see cref="ToolTip"/> style of <c>App.axaml</c>); always set (0 = Regular).</summary>
    public const string WeightResourceKey = "SignetToolTipFontWeight";

    /// <summary>The area whose tooltip font the control and its descendants use; <c>null</c> = the enclosing one.</summary>
    public static readonly AttachedProperty<TooltipArea?> AreaProperty =
        AvaloniaProperty.RegisterAttached<Control, TooltipArea?>("Area", typeof(TooltipFonts));

    // The area roots, to apply a change to; the table does not keep the controls alive.
    private static readonly ConditionalWeakTable<Control, object> Roots = new();
    private static Func<TooltipArea, TooltipFont> _source = _ => TooltipFont.Default;

    static TooltipFonts()
    {
        AreaProperty.Changed.AddClassHandler<Control>((control, _) => Apply(control));
    }

    /// <summary>Gets <see cref="AreaProperty"/>.</summary>
    public static TooltipArea? GetArea(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(AreaProperty);
    }

    /// <summary>Sets <see cref="AreaProperty"/>.</summary>
    public static void SetArea(Control control, TooltipArea? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(AreaProperty, value);
    }

    /// <summary>
    /// Sets where the fonts come from (the settings) and applies them to the application and to every area root.
    /// Called at startup and after the tooltip fonts or the interface font changed.
    /// </summary>
    public static void Apply(Func<TooltipArea, TooltipFont> source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (Application.Current is { } app)
        {
            ApplyTo(app);
        }

        List<Control> roots = new();
        foreach (KeyValuePair<Control, object> root in Roots)
        {
            roots.Add(root.Key);
        }

        foreach (Control root in roots)
        {
            Apply(root);
        }
    }

    /// <summary>
    /// Starts over for a new application (<see cref="App.Initialize"/>): forgets the area roots of an earlier one and
    /// applies the default fonts until the saved ones are applied. In the application it runs once, with no roots yet;
    /// the headless tests create an application per test, and without it each new one restyled every root the earlier
    /// tests had left, which made each test start slower than the one before.
    /// </summary>
    public static void Reset()
    {
        Roots.Clear();
        Apply(_ => TooltipFont.Default);
    }

    /// <summary>The typeface of an area's tooltips: the chosen one, or the interface font in use.</summary>
    public static FontFamily FamilyOf(TooltipFont font) =>
        !string.IsNullOrWhiteSpace(font.FontFamily)
            ? FontFamily.Parse(font.FontFamily)
            : Application.Current?.TryGetResource("ContentControlThemeFontFamily", null, out object? family) == true
              && family is FontFamily interfaceFont
                ? interfaceFont
                : FontFamily.Default;

    private static void ApplyTo(Application app)
    {
        TooltipFont font = _source(TooltipArea.OtherWindows);
        app.Resources[FamilyResourceKey] = FamilyOf(font);
        app.Resources[SizeResourceKey] = (double)font.FontSize;
        app.Resources[WeightResourceKey] = FontWeightOptions.ToAvalonia(font.FontWeight);
    }

    private static void Apply(Control control)
    {
        if (GetArea(control) is not { } area)
        {
            control.Resources.Remove(FamilyResourceKey);
            control.Resources.Remove(SizeResourceKey);
            control.Resources.Remove(WeightResourceKey);
            Roots.Remove(control);
            return;
        }

        TooltipFont font = _source(area);
        control.Resources[FamilyResourceKey] = FamilyOf(font);
        control.Resources[SizeResourceKey] = (double)font.FontSize;
        control.Resources[WeightResourceKey] = FontWeightOptions.ToAvalonia(font.FontWeight);
        Roots.AddOrUpdate(control, area);
    }
}
