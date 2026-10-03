using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Input;
using Signet.App.Services;
using Signet.App.Tabs;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.Core.Misc;
using Signet.Core.Spellcheck;

namespace Signet.App.Infrastructure;

/// <summary>
/// Composition of the application's dependencies. A lightweight <see cref="ServiceCollection"/>
/// container (no generic host) built once at startup in <c>Program</c>.
/// </summary>
internal static class AppServices
{
    /// <summary>Builds the application's service provider.</summary>
    public static ServiceProvider Build(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        ServiceCollection services = new();

        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        services.AddSingleton(_ => new SettingsStore());
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<LocalizationManager>();
        services.AddSingleton<IconThemeManager>();
        services.AddSingleton<UiDensityManager>();
        services.AddSingleton<SpellChecker>();

        services.AddSingleton<IStatusBarService, StatusBarService>();
        services.AddSingleton<KeyboardShortcutManager>();
        services.AddSingleton<ToolbarManager>();
        services.AddSingleton<AppActionRegistry>();
        services.AddSingleton<BookBrowserViewModel>();
        services.AddSingleton<PreviewViewModel>();
        services.AddSingleton<MainDockFactory>();
        services.AddSingleton<TabManager>();
        services.AddSingleton<ClipboardHistoryService>();
        services.AddTransient<ToolbarCustomizeViewModel>();
        services.AddTransient<PreferencesViewModel>();

        services.AddTransient<MainWindowViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
