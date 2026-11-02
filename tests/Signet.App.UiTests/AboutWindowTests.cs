using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>
/// About window: version &lt;VersionPrefix&gt;-&lt;timestamp&gt; compiled in by Versioning.targets. The prefix is read from
/// the test assembly (the same VersionPrefix of Directory.Build.props), so bumping the version does not break the test.
/// </summary>
public sealed class AboutWindowTests
{
    [AvaloniaFact]
    public void Shows_the_compiled_in_version()
    {
        // Arrange
        string prefix = typeof(AboutWindowTests).Assembly.GetName().Version!.ToString(3);
        AboutWindow window = new();

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        window.FindControl<SelectableTextBlock>("VersionText")!.Text
            .Should().MatchRegex($@"^{Regex.Escape(prefix)}-\d{{12}}$");
        window.Close();
    }
}
