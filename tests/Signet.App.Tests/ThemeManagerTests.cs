using System;
using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Theme layer tests (without starting Avalonia: <see cref="ThemeManager"/> skips applying the
/// variant when there is no <c>Application.Current</c>).
/// </summary>
public sealed class ThemeManagerTests : IDisposable
{
    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), "Signet.Tests", $"theme-{Guid.NewGuid():N}.json");

    [Fact]
    public void Cycle_advances_System_Light_Dark_and_persists()
    {
        SettingsStore settings = new(_settingsPath);
        ThemeManager sut = new(settings, NullLogger<ThemeManager>.Instance);

        sut.Current.Should().Be(ThemePreference.System);
        sut.Cycle().Should().Be(ThemePreference.Light);
        sut.Cycle().Should().Be(ThemePreference.Dark);
        sut.Cycle().Should().Be(ThemePreference.System);

        new SettingsStore(_settingsPath).ThemePreference.Should().Be(ThemePreference.System);
    }

    [Fact]
    public void MainWindowViewModel_CycleThemeCommand_updates_CurrentTheme()
    {
        SettingsStore settings = new(_settingsPath);
        ThemeManager theme = new(settings, NullLogger<ThemeManager>.Instance);
        MainWindowViewModel sut = new(theme, NullLogger<MainWindowViewModel>.Instance);

        sut.CurrentTheme.Should().Be(ThemePreference.System);
        sut.CycleThemeCommand.Execute(null);
        sut.CurrentTheme.Should().Be(ThemePreference.Light);
        settings.ThemePreference.Should().Be(ThemePreference.Light);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }
        }
        catch (IOException)
        {
            // best-effort
        }
    }
}
