using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Views;
using System.Linq;

namespace Signet.App.UiTests;

/// <summary>The thin frame close to the edge of every dialog window (a style in <c>App.axaml</c>).</summary>
public sealed class DialogFrameTests
{
    private static ContentPresenter Presenter(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        return window.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
    }

    [AvaloniaFact]
    public void A_dialog_has_a_thin_frame_in_the_color_of_the_theme_close_to_its_edge()
    {
        // Arrange
        ConfirmWindow dialog = new();

        // Act
        ContentPresenter presenter = Presenter(dialog);

        // Assert
        presenter.BorderThickness.Should().Be(new Avalonia.Thickness(1));
        presenter.Margin.Should().Be(new Avalonia.Thickness(4), "close to the edge, not on it");
        presenter.BorderBrush.Should().BeOfType<SolidColorBrush>().Which.Color
            .Should().Be(AppTheme.Current.Color(Signet.Core.Themes.ThemeKeys.UiFrame));
        dialog.Close();
    }

    [AvaloniaFact]
    public void A_window_that_opts_out_has_no_frame()
    {
        // Arrange
        Window window = new() { Classes = { "noFrame" }, Content = new TextBlock { Text = "x" } };

        // Act
        ContentPresenter presenter = Presenter(window);

        // Assert
        presenter.BorderBrush.Should().BeNull();
        presenter.Margin.Should().Be(new Avalonia.Thickness(0));
        window.Close();
    }
}
