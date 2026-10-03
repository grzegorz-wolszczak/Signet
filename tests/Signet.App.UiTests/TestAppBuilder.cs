using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Signet.App.UiTests.TestAppBuilder))]

namespace Signet.App.UiTests;

/// <summary>
/// The entry point of the Avalonia application for headless tests. Uses the real <see cref="App"/>
/// class (the Fluent theme + Dock.Avalonia), but without the desktop lifetime —
/// the tests create and show the windows themselves.
/// </summary>
public static class TestAppBuilder
{
    /// <summary>Builds the application on the headless platform (a substitute renderer, the built-in font manager).</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
