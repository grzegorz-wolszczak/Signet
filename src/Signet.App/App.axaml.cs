using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Signet.App.Infrastructure;
using Signet.App.Input;
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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // The default tooltip font (the interface font, 13 px) and the colors of Signet Light until the saved ones are
        // applied — also in tests.
        TooltipFonts.Apply(_ => TooltipFont.Default);
        AppTheme.Apply(this, ResolvedTheme.Defaults);
    }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IServiceProvider services = Services
                ?? throw new InvalidOperationException("App.Services was not set before Avalonia started.");

            // The language first (the theme made from the old color settings gets a name in it); before icons: the toolbar
            // icon size is computed from the UI font height, and the theme may name the icon set.
            services.GetRequiredService<LocalizationManager>().ApplySaved();
            services.GetRequiredService<ThemeManager>().ApplySaved();
            services.GetRequiredService<UiDensityManager>().ApplySaved();
            services.GetRequiredService<IconThemeManager>().ApplySaved(AppTheme.Current.IconSet);

            SettingsStore settings = services.GetRequiredService<SettingsStore>();
            DebugLog.IsEnabled = settings.DebugLogging;
            AltGrKeys.Enabled = settings.EnableAltGr;
            UiEventLogger.Register();
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
        // Corrected (centered on the primary monitor) when the remembered place is no longer fully on screen.
        if (settings.GetWindowGeometry(MainWindowGeometryKey) is { } geometry)
        {
            WindowPlacement.Restore(window, geometry);
        }
    }

    private static void SaveGeometry(Window window, SettingsStore settings)
    {
        settings.SetWindowGeometry(MainWindowGeometryKey, WindowPlacement.Capture(window));
        settings.Save();
    }
}
