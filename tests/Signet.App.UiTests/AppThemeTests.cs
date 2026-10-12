using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.Core.Misc;
using Signet.Core.Themes;

namespace Signet.App.UiTests;

/// <summary>The theme in use applied to the application (<see cref="AppTheme"/>, <see cref="ThemeManager"/>).</summary>
public sealed class AppThemeTests
{
    [AvaloniaFact]
    public void Signet_dark_turns_the_controls_dark_and_recolors_the_interface_live()
    {
        using TempDir dir = new();
        ThemeStore store = new(dir.Path);
        TextBlock marker = new() { Text = "⚠" };
        marker.Bind(TextBlock.ForegroundProperty, marker.GetResourceObservable("SignetRiskMarkerBrush"));
        Button danger = new() { Content = "Delete", Classes = { "danger" } };
        Window window = new() { Content = new StackPanel { Children = { marker, danger } } };
        int changed = 0;
        void OnChanged(object? sender, EventArgs e) => changed++;
        AppTheme.Changed += OnChanged;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Color lightMarker = ((ISolidColorBrush)marker.Foreground!).Color;

            AppTheme.Apply(Application.Current!, new ResolvedTheme(store.Get(ThemeStore.DarkId), store.Resolver));
            Dispatcher.UIThread.RunJobs();

            changed.Should().Be(1);
            Application.Current!.ActualThemeVariant.Should().Be(Avalonia.Styling.ThemeVariant.Dark);
            ((ISolidColorBrush)marker.Foreground!).Color.Should().Be(Color.Parse("#F14C4C")).And.NotBe(lightMarker);
            ((ISolidColorBrush)danger.Background!).Color.Should().Be(Color.Parse("#C42B1C"));
            ActionIcons.AttentionColor.Should().Be(Color.Parse("#F1707B"));
            Application.Current.Resources[AppTheme.FolderIconKey].Should().BeOfType<DrawingImage>();
        }
        finally
        {
            AppTheme.Changed -= OnChanged;
            AppTheme.Apply(Application.Current!, ResolvedTheme.Defaults);
            window.Close();
        }

        Application.Current!.ActualThemeVariant.Should().Be(Avalonia.Styling.ThemeVariant.Light);
        ActionIcons.AttentionColor.Should().Be(ActionIcons.DefaultAttentionColor);
    }

    [AvaloniaFact]
    public void The_fluent_palette_and_the_accent_of_a_theme_go_to_the_palette_of_its_variant()
    {
        Theme theme = new Theme(Ulid.NewUlid().ToString(), "Palette") { ParentId = ThemeStore.LightId }
            .With(ThemeKeys.UiAccent.Id, ThemeEntry.Of(ThemeColor.Parse("#336699")))
            .With(ThemeKeys.FluentPaletteKeyId("RegionColor"), ThemeEntry.Of(ThemeColor.Parse("#FAF4E8")));
        try
        {
            AppTheme.Apply(Application.Current!, new ResolvedTheme(theme, new ThemeResolver(_ => null)));

            ColorPaletteResources palette = Application.Current!.Styles.OfType<FluentTheme>().Single()
                .Palettes[Avalonia.Styling.ThemeVariant.Light];
            palette.Accent.Should().Be(Color.Parse("#336699"));
            palette.RegionColor.Should().Be(Color.Parse("#FAF4E8"));
        }
        finally
        {
            AppTheme.Apply(Application.Current!, ResolvedTheme.Defaults);
        }
    }

    [AvaloniaFact]
    public void A_theme_that_no_longer_exists_gives_way_to_signet_light()
    {
        using TempDir dir = new();
        string settingsPath = System.IO.Path.Combine(dir.Path, "settings.json");
        SettingsStore settings = new(settingsPath) { UiThemeId = Ulid.NewUlid().ToString() };
        ThemeManager sut = new(settings, new ThemeStore(System.IO.Path.Combine(dir.Path, "themes")), NullLogger<ThemeManager>.Instance);
        try
        {
            sut.ApplySaved();

            AppTheme.Current.Theme.Id.Should().Be(ThemeStore.LightId);
        }
        finally
        {
            AppTheme.Apply(Application.Current!, ResolvedTheme.Defaults);
        }
    }

    [AvaloniaFact]
    public void The_drawn_icons_take_the_hue_of_the_attention_color()
    {
        Color blue = Color.FromRgb(0x1E, 0x88, 0xE5);
        Color red = ActionIcons.ToAttention(blue);
        try
        {
            ActionIcons.SetStateColors(ActionIcons.DefaultInactiveColor, Color.Parse("#2E7D32"));

            Color green = ActionIcons.ToAttention(blue);

            green.ToHsl().H.Should().BeApproximately(Color.Parse("#2E7D32").ToHsl().H, 1);
            red.ToHsl().H.Should().BeApproximately(0, 1, "the default attention color is red");
        }
        finally
        {
            ActionIcons.SetStateColors(ActionIcons.DefaultInactiveColor, ActionIcons.DefaultAttentionColor);
        }
    }

    // A folder removed with the test.
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Directory.CreateTempSubdirectory("signet-theme-").FullName;

        public void Dispose() => System.IO.Directory.Delete(Path, recursive: true);
    }
}
