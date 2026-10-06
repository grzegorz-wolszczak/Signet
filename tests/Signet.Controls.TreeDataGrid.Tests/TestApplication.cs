using Signet.Controls.TreeDataGrid.Tests;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;

[assembly: AvaloniaTestApplication(typeof(TestApplication))]

namespace Signet.Controls.TreeDataGrid.Tests
{
    public class TestApplication : Application
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
    }
}
