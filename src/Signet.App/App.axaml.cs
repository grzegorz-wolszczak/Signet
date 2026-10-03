using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;

namespace Signet.App;

/// <summary>Avalonia application class. Creates the main window once framework initialization completes.</summary>
public partial class App : Application
{
    private const string MainWindowGeometryKey = "MainWindow";

    /// <summary>
    /// Service provider built in <see cref="Program"/>. Set before Avalonia starts.
    /// </summary>
    public static IServiceProvider? Services { get; set; }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IServiceProvider services = Services
                ?? throw new InvalidOperationException("App.Services was not set before Avalonia started.");

            services.GetRequiredService<ThemeManager>().ApplySaved();
            // Before icons: toolbar icon size is computed from the UI font height.
            services.GetRequiredService<UiDensityManager>().ApplySaved();
            services.GetRequiredService<LocalizationManager>().ApplySaved();
            services.GetRequiredService<IconThemeManager>().ApplySaved();

            SettingsStore settings = services.GetRequiredService<SettingsStore>();
            MainWindowViewModel viewModel = services.GetRequiredService<MainWindowViewModel>();
            viewModel.StartupFilePath = StartupArguments.FileToOpen(desktop.Args, Environment.CurrentDirectory);
            MainWindow window = new()
            {
                DataContext = viewModel,
            };

            RestoreGeometry(window, settings);
            window.Closing += (_, _) => SaveGeometry(window, settings);

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RestoreGeometry(Window window, SettingsStore settings)
    {
        WindowGeometry? geometry = settings.GetWindowGeometry(MainWindowGeometryKey);
        if (geometry is not { } g || g.Width <= 0 || g.Height <= 0)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(g.X, g.Y);
        window.Width = Math.Max(g.Width, window.MinWidth);
        window.Height = Math.Max(g.Height, window.MinHeight);
        window.WindowState = g switch
        {
            { FullScreen: true } => WindowState.FullScreen,
            { Maximized: true } => WindowState.Maximized,
            _ => WindowState.Normal,
        };
    }

    private static void SaveGeometry(Window window, SettingsStore settings)
    {
        // Width/Height are logical units (DIP) — the same ones RestoreGeometry assigns back
        // to window.Width/window.Height. Position is in physical pixels (PixelPoint),
        // matching how RestoreGeometry reads it.
        settings.SetWindowGeometry(MainWindowGeometryKey, new WindowGeometry(
            window.Position.X,
            window.Position.Y,
            (int)window.Width,
            (int)window.Height,
            Maximized: window.WindowState == WindowState.Maximized,
            FullScreen: window.WindowState == WindowState.FullScreen));

        settings.Save();
    }
}
