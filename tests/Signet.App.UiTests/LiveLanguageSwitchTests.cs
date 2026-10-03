using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Views;
using Signet.Core.Misc;

namespace Signet.App.UiTests;

/// <summary>
/// Switching the UI language live: the XAML texts (<c>{loc:Loc …}</c>) are bindings to
/// <see cref="Localizer"/> and change in open windows without a restart.
/// </summary>
public sealed class LiveLanguageSwitchTests
{
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Open_window_texts_follow_a_language_switch()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pl");
            TextPromptWindow window = new();
            window.Show();
            Render(window);
            window.GetVisualDescendants().OfType<Button>().Select(b => b.Content).Should().Contain("Anuluj");

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Strings.NotifyLanguageChanged();
            Render(window);

            window.GetVisualDescendants().OfType<Button>().Select(b => b.Content).Should().Contain("Cancel");
            window.Close();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            Strings.NotifyLanguageChanged();
        }
    }

    [AvaloniaFact]
    public void ApplySaved_notifies_only_when_the_language_actually_changes()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        string root = Path.Combine(Path.GetTempPath(), "Signet.Tests", "lang-" + Guid.NewGuid().ToString("N"));
        SettingsStore settings = new(Path.Combine(root, "settings.json"));
        LocalizationManager manager = new(settings, NullLogger<LocalizationManager>.Instance);
        Listener listener = new();
        Strings.RegisterLanguageAware(listener);
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pl");
            manager.Set("pl");
            manager.ApplySaved();
            listener.Calls.Should().Be(0);

            manager.Set("en");
            manager.ApplySaved();
            listener.Calls.Should().Be(1);
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Should().Be("en");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            Strings.NotifyLanguageChanged();
        }
    }

    private sealed class Listener : ILanguageAware
    {
        public int Calls { get; private set; }

        public void OnLanguageChanged() => Calls++;
    }
}
