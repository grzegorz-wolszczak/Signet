using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using NativeWebView.Core;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>Headless tests of <see cref="PreviewView"/>.</summary>
public sealed class PreviewViewTests
{
    // Dock detaches and re-attaches the same view when an auto-hidden Preview flyout is hidden and shown
    // again; the web view must survive that (it used to be disposed on detach — "Cannot access a disposed
    // object: NativeWebViewController" on the next show).
    [AvaloniaFact]
    public void Web_view_survives_leaving_and_reentering_the_visual_tree()
    {
        PreviewView view = new();
        Window window = new() { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        NativeWebView.Controls.NativeWebView web = view.GetVisualDescendants().OfType<NativeWebView.Controls.NativeWebView>().Single();

        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        web.LifecycleState.Should().NotBe(NativeWebComponentState.Disposed);

        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        web.LifecycleState.Should().NotBe(NativeWebComponentState.Disposed);
    }
}
