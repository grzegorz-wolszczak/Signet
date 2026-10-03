using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Infrastructure;

namespace Signet.App.UiTests;

/// <summary>Recoloring real action icons (the "Save" floppy) through <see cref="ActionIcons"/>.</summary>
public sealed class ActionIconsTests
{
    private static IEnumerable<Color> SolidColors(Drawing drawing) => drawing switch
    {
        DrawingGroup g => g.Children.SelectMany(SolidColors),
        GeometryDrawing { Brush: ISolidColorBrush b } when b.Color.A > 0 => new[] { b.Color },
        _ => Enumerable.Empty<Color>(),
    };

    [AvaloniaFact]
    public void Save_icon_variants_are_grey_or_red_and_cached()
    {
        // The icon set is loaded only by the desktop app startup, so the bundled "main" set is merged in temporarily here.
        var icons = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://Signet.App/Assets/Icons/MainIcons.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(icons);
        try
        {
            AssertVariants();
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(icons);
        }
    }

    private static void AssertVariants()
    {
        var normal = (DrawingImage)ActionIcons.Resolve("document-save", ActionIconState.Normal)!;
        var inactive = (DrawingImage)ActionIcons.Resolve("document-save", ActionIconState.Inactive)!;
        var attention = (DrawingImage)ActionIcons.Resolve("document-save", ActionIconState.Attention)!;

        inactive.Should().NotBeSameAs(normal);
        ActionIcons.Resolve("document-save", ActionIconState.Inactive).Should().BeSameAs(inactive);

        SolidColors(inactive.Drawing!).Should().OnlyContain(c => c.R == c.G && c.G == c.B);
        SolidColors(attention.Drawing!)
            .Where(c => System.Math.Max(c.R, System.Math.Max(c.G, c.B)) - System.Math.Min(c.R, System.Math.Min(c.G, c.B)) >= 40)
            .Should().NotBeEmpty()
            .And.OnlyContain(c => c.R > c.G && c.R > c.B, "colored parts must be red");
    }
}
