using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;

namespace Signet.App.UiTests;

/// <summary>
/// Headless tests of the "Preferences" window — in particular the visual separation of the tab list
/// from the content area (in the light theme both parts were white and blended into one surface).
/// </summary>
public sealed class PreferencesWindowRenderTests
{
    private static PreferencesWindow BuildWindow()
    {
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "prefs-ui-" + Guid.NewGuid().ToString("N"));
        SettingsStore settings = new(Path.Combine(root, "settings.json"));
        SpellChecker spellChecker = new(settings, Path.Combine(root, "hunspell"), Path.Combine(root, "user"));
        ThemeManager theme = new(settings, NullLogger<ThemeManager>.Instance);
        LocalizationManager localization = new(settings, NullLogger<LocalizationManager>.Instance);
        IconThemeManager iconTheme = new(settings, NullLogger<IconThemeManager>.Instance);
        KeyboardShortcutManager shortcuts = new(settings);
        using StatusBarService statusBar = new();
        AppActionRegistry registry = new(shortcuts, statusBar, NullLogger<AppActionRegistry>.Instance);

        UiDensityManager uiDensity = new(settings, iconTheme, NullLogger<UiDensityManager>.Instance);
        PreferencesViewModel vm = new(settings, spellChecker, theme, localization, iconTheme, shortcuts, registry, uiDensity);
        return new PreferencesWindow { DataContext = vm };
    }

    /// <summary>
    /// Regression: the tab content area must have its own background and border, and the tab strip a dimmed
    /// background from TabControl. The test also guards against a rename of a template part in Avalonia: if
    /// PART_SelectedContentHost disappeared, the style would silently stop applying.
    /// </summary>
    [AvaloniaFact]
    public void Tab_strip_and_content_area_are_visually_separated()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.Background.Should().NotBeNull("the tab strip must differ from the content");

        ContentPresenter content = window.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .Single(c => c.Name == "PART_SelectedContentHost");

        content.BorderThickness.Should().Be(new Avalonia.Thickness(1));
        content.BorderBrush.Should().NotBeNull();
        content.Background.Should().NotBeNull();
        content.Background.Should().NotBeSameAs(tabs.Background, "the content has a different background than the tab strip");
    }

    /// <summary>
    /// The sections of the "Appearance" tab (Preview fonts, the special character font, Code View
    /// light and dark) should be discrete cards with a border, so they do not blend into one list.
    /// </summary>
    [AvaloniaFact]
    public void Appearance_sections_are_drawn_as_bordered_cards()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 1; // Appearance
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        Border[] sections = window.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Classes.Contains("section"))
            .ToArray();

        sections.Should().HaveCount(3);
        sections.Should().OnlyContain(b => b.BorderBrush != null);
        sections.Should().OnlyContain(b => b.BorderThickness == new Avalonia.Thickness(1));
    }

    /// <summary>
    /// The "Keyboard Shortcuts" tab: sections by category, a row editor after
    /// expanding, and capturing the combination pressed on the shortcut button.
    /// </summary>
    [AvaloniaFact]
    public void Keyboard_shortcut_editor_captures_a_pressed_key_combination()
    {
        PreferencesWindow window = BuildWindow();
        var vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 5; // Keyboard Shortcuts
        ShortcutRow save = vm.Shortcuts.Single(r => r.Id == AppActionIds.Save);
        vm.ShortcutGroups.Single(g => g.Rows.Contains(save)).IsExpanded = true;
        save.ToggleEditorCommand.Execute(null);
        save.IsCustom = true;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        window.GetVisualDescendants().OfType<Expander>().Should().HaveCount(vm.ShortcutGroups.Count);
        Border editor = window.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("shortcutEditor") && b.IsVisible);
        Button capture = editor.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content is string text && text == save.CaptureButtonText);

        capture.Focus();
        save.BeginCapture();
        window.KeyPress(Avalonia.Input.Key.F9, Avalonia.Input.RawInputModifiers.Shift,
            Avalonia.Input.PhysicalKey.F9, null);
        Dispatcher.UIThread.RunJobs();

        save.IsCapturing.Should().BeFalse();
        save.GestureText.Should().Be("Shift+F9");
    }

    /// <summary>
    /// The "Spellcheck" tab: dictionaries with language names, the list of user
    /// dictionaries with a toggle, the selected dictionary as the default, the list of its words with actions
    /// available after selecting a word, and a button that opens the settings folder.
    /// </summary>
    [AvaloniaFact]
    public void Spellcheck_tab_manages_user_dictionaries_and_their_words()
    {
        PreferencesWindow window = BuildWindow();
        var vm = (PreferencesViewModel)window.DataContext!;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 4; // Spellcheck
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        ComboBox primary = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "PrimaryDictionaryBox");
        primary.SelectedItem.Should().Be(new DictionaryOption("en_US", SpellChecker.DisplayName("en_US")));
        primary.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain(SpellChecker.DisplayName("en_US"));

        ListBox dictionaries = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "UserDictionaryList");
        dictionaries.SelectedItem.Should().Be(vm.SelectedUserDictionary);
        dictionaries.GetVisualDescendants().OfType<CheckBox>().Should().ContainSingle(c => c.IsChecked == true);
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DefaultUserDictionaryText")
            .Text.Should().Be(vm.DefaultUserDictionaryText);
        window.GetVisualDescendants().OfType<Button>().Should().Contain(b => b.Name == "OpenPreferencesLocationButton");

        TabControl inner = window.GetVisualDescendants().OfType<TabControl>().Single(t => t != tabs);
        vm.SelectedUserDictionary!.Name.Should().Be(SpellChecker.DefaultUserDictionaryName);
        inner.SelectedIndex = 1; // the word list
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        Button remove = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RemoveUserWordsButton");
        remove.IsEffectivelyEnabled.Should().BeFalse("without a selected word there is nothing to remove");
    }

    /// <summary>
    /// The font picker window: every name in the list is rendered in its own font,
    /// and the current font is selected.
    /// </summary>
    [AvaloniaFact]
    public void Font_picker_renders_every_font_name_in_its_own_font()
    {
        FontEntry[] fonts = { new("Arial", false), new("Consolas", true), new("Georgia", false) };
        FontPickerWindow window = new()
        {
            DataContext = new FontPickerViewModel(fonts, new FontPickRequest("Georgia", 12, false)),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        ListBox list = window.GetVisualDescendants().OfType<ListBox>().Single();
        list.SelectedItem.Should().Be(fonts[2]);
        TextBlock[] names = list.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => fonts.Any(f => f.Name == t.Text))
            .ToArray();
        names.Should().HaveCount(fonts.Length);
        names.Should().OnlyContain(t => t.FontFamily.Name == t.Text);
    }

    /// <summary>
    /// The labels on the "General" tab (and the other "label + field" grids) get 2/3
    /// of the width and wrap — previously long Polish texts hid under the fields.
    /// </summary>
    [AvaloniaFact]
    public void Labeled_grids_give_two_thirds_to_the_wrapping_label()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 3; // General
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        Grid grid = window.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("labeled") && g.IsEffectivelyVisible);
        double label = grid.ColumnDefinitions[0].ActualWidth;
        double control = grid.ColumnDefinitions[1].ActualWidth;

        (label / (label + control)).Should().BeInRange(0.6, 0.7, "about 2/3 for the label");
        grid.Children.OfType<TextBlock>().Should().OnlyContain(t => t.TextWrapping == TextWrapping.Wrap);
    }

    /// <summary>The font fields in Preferences show the name rendered in that font.</summary>
    [AvaloniaFact]
    public void Preferences_font_fields_render_the_name_in_the_font_itself()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 1; // Appearance
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        TextBlock[] fields = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("fontField") && !b.Classes.Contains("uiFont"))
            .Select(b => (TextBlock)b.Child!)
            .ToArray();

        fields.Should().HaveCount(3, "special character + Code View light and dark; the preview fonts are on the Preview tab");

        fields.Should().OnlyContain(t => !string.IsNullOrEmpty(t.Text) && t.FontFamily.Name == t.Text);
    }

    /// <summary>
    /// The "Preview" tab (after "Appearance"): the preview fonts, the refresh delay and the highlighting
    /// of the cursor position — as cards; font fields in their own font, color fields in their own color.
    /// </summary>
    [AvaloniaFact]
    public void Preview_tab_groups_fonts_refresh_and_highlight_settings()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.SelectedIndex = 2; // Preview
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        ((TabItem)tabs.SelectedItem!).Header.Should().Be(Strings.Get("PreferencesWindow_Preview"));
        window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("section"))
            .Should().HaveCount(3);

        TextBlock[] fonts = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("fontField"))
            .Select(b => (TextBlock)b.Child!)
            .ToArray();
        fonts.Should().HaveCount(3).And.OnlyContain(t => t.FontFamily.Name == t.Text);

        TextBox[] colors = window.GetVisualDescendants().OfType<TextBox>()
            .Where(t => t.Classes.Contains("colorValue"))
            .ToArray();
        colors.Should().HaveCount(2);
        colors.Should().OnlyContain(t => ((ISolidColorBrush)t.Background!).Color == Color.Parse(PreviewHighlight.Default.LightColor));

        window.GetVisualDescendants().OfType<TextBlock>()
            .Should().Contain(t => t.Text == Strings.Get("PreferencesWindow_PreviewRefreshDelay"))
            .And.Contain(t => t.Text == Strings.Get("PreferencesWindow_PreviewHighlightColorDark"));
    }

    /// <summary>The Code View color fields have a background in the color of the entered value — also when focused — and readable text.</summary>
    [AvaloniaFact]
    public void Code_view_color_fields_are_painted_with_their_own_color()
    {
        PreferencesWindow window = BuildWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1; // Appearance
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        TextBox[] fields = window.GetVisualDescendants().OfType<TextBox>()
            .Where(t => t.Classes.Contains("colorValue"))
            .ToArray();
        fields.Should().NotBeEmpty();

        TextBox dark = fields.First(t => t.Text == "#000080");
        ((ISolidColorBrush)dark.Background!).Color.Should().Be(Color.Parse("#000080"));
        ((ISolidColorBrush)dark.Foreground!).Color.Should().Be(Colors.White);

        TextBox light = fields.First(t => t.Text == "#FFFFBF");
        ((ISolidColorBrush)light.Foreground!).Color.Should().Be(Colors.Black);

        dark.Focus();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Border frame = dark.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
        ((ISolidColorBrush)frame.Background!).Color.Should().Be(Color.Parse("#000080"), "focus must not restore the theme background");
    }

    /// <summary>An invalid color value does not paint the field — the theme look returns.</summary>
    [AvaloniaFact]
    public void Invalid_color_value_leaves_the_theme_background()
    {
        ColorConverters.ToBrush.Convert("#zzz", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(Avalonia.AvaloniaProperty.UnsetValue);
    }

    /// <summary>Changing the UI font in Preferences takes effect immediately, without a restart.</summary>
    [AvaloniaFact]
    public void Ui_font_is_applied_live_and_reset_restores_the_default()
    {
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "uifont-" + Guid.NewGuid().ToString("N"));
        SettingsStore settings = new(Path.Combine(root, "settings.json"));
        UiDensityManager manager = new(
            settings, new IconThemeManager(settings, NullLogger<IconThemeManager>.Instance), NullLogger<UiDensityManager>.Instance);
        TextBlock text = new() { Text = "Wycinki" };
        Window window = new() { Content = text };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        double before = text.FontSize;

        try
        {
            manager.ApplyUiFont("Tahoma", 17);
            Dispatcher.UIThread.RunJobs();

            text.FontSize.Should().Be(17);
            text.FontFamily.Name.Should().Be("Tahoma");
        }
        finally
        {
            manager.ApplyUiFont(string.Empty, 0);
            Dispatcher.UIThread.RunJobs();
        }

        text.FontSize.Should().Be(before);
    }
}
