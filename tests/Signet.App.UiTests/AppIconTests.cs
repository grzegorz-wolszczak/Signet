using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>The application icon (a green "S") set by the <c>:is(Window)</c> style in <c>App.axaml</c>.</summary>
public sealed class AppIconTests
{
    [AvaloniaFact]
    public void Every_window_gets_the_application_icon()
    {
        Window plain = new();
        FontPickerWindow dialog = new();

        plain.Show();
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        plain.Icon.Should().NotBeNull();
        dialog.Icon.Should().NotBeNull("dialogs must also have the application icon, not Avalonia's default one");
        dialog.Close();
        plain.Close();
    }
}
