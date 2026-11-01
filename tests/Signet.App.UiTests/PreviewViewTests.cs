using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using NativeWebView.Core;
using Avalonia;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Core.Misc;

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
        // Arrange
        PreviewView view = new();
        Window window = new() { Content = view };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        NativeWebView.Controls.NativeWebView web = view.GetVisualDescendants().OfType<NativeWebView.Controls.NativeWebView>().Single();

        window.Content = null;
        Dispatcher.UIThread.RunJobs();

        // Assert
        web.LifecycleState.Should().NotBe(NativeWebComponentState.Disposed);

        // Act
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Assert
        web.LifecycleState.Should().NotBe(NativeWebComponentState.Disposed);
    }

    /// <summary>
    /// The bar at the bottom of the panel is a toolbar like any other (ToolbarId.Preview), with the zoom after it;
    /// its look is its own, put into the resources of the border that holds it (which come before the application's,
    /// where the look of the main window's toolbars is).
    /// </summary>
    [AvaloniaFact]
    public void The_bottom_bar_is_the_panels_own_toolbar_with_a_look_of_its_own()
    {
        // Arrange
        using MainWindowViewModel main = new();
        PreviewView view = new() { DataContext = main.Preview };
        Window window = new() { Width = 600, Height = 400, Content = view };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        ToolbarStripView strip = view.GetVisualDescendants().OfType<ToolbarStripView>().Single();
        Border host = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToolbarHost");

        // Assert
        strip.DataContext.Should().BeSameAs(main.PreviewToolbar);
        host.Resources[ToolbarManager.IconSizeResourceKey].Should()
            .Be((double)main.PreviewToolbar.Appearance.IconPixels);

        // Act
        main.PreviewToolbar.SetIconSizeCommand.Execute(ToolbarIconSize.Big);
        Dispatcher.UIThread.RunJobs();

        // Assert
        host.Resources[ToolbarManager.IconSizeResourceKey].Should().Be((double)ToolbarAppearance.BigIconPixels);
        Application.Current!.Resources.TryGetValue(ToolbarManager.IconSizeResourceKey, out object? app).Should().BeTrue();
        app.Should().Be((double)ToolbarAppearance.MediumIconPixels, "the main window's toolbars keep their own size");

        window.Close();
    }

    /// <summary>
    /// While no book is open only the page is grayed out: the toolbar keeps its context menu, so its buttons, the
    /// position of their texts and the size of their icons can be set without a book (the buttons are actions of the
    /// book and gray out on their own).
    /// </summary>
    [AvaloniaFact]
    public void The_toolbar_stays_usable_while_no_book_is_open()
    {
        // Arrange
        using MainWindowViewModel main = new();
        PreviewView view = new() { DataContext = main.Preview };
        Window window = new() { Width = 600, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();

        // Act
        main.Preview.IsContentEnabled = false;
        Dispatcher.UIThread.RunJobs();
        ToolbarStripView strip = view.GetVisualDescendants().OfType<ToolbarStripView>().Single();
        Border host = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToolbarHost");
        Control page = view.GetVisualDescendants().OfType<NativeWebView.Controls.NativeWebView>().Single();

        // Assert
        page.IsEffectivelyEnabled.Should().BeFalse("the page is grayed out without a book");
        host.IsEffectivelyEnabled.Should().BeTrue();
        strip.IsEffectivelyEnabled.Should().BeTrue();
        strip.ContextMenu.Should().NotBeNull("the toolbar menu is the only way to configure this toolbar");

        window.Close();
    }
}
