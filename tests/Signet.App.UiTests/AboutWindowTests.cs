using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>About window: version 0.6.0-&lt;timestamp&gt; compiled in by Versioning.targets.</summary>
public sealed class AboutWindowTests
{
    [AvaloniaFact]
    public void Shows_the_compiled_in_version()
    {
        AboutWindow window = new();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        window.FindControl<SelectableTextBlock>("VersionText")!.Text
            .Should().MatchRegex(@"^0\.6\.0-\d{12}$");
        window.Close();
    }
}
