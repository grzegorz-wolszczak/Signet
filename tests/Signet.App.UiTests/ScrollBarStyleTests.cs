using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;

namespace Signet.App.UiTests;

/// <summary>
/// Scroll bars in the whole application: always expanded to their full width (Fluent hides them as thin lines until
/// the pointer comes near, which made them hard to hit) and beside the content, not drawn over it.
/// </summary>
public sealed class ScrollBarStyleTests
{
    private static (Window Window, T Control) Show<T>(T control)
        where T : Control
    {
        Window window = new() { Width = 300, Height = 200, Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return (window, control);
    }

    private static void ShouldBeExpandedBesideTheContent(Control host)
    {
        ScrollViewer viewer = host.GetSelfAndVisualDescendants().OfType<ScrollViewer>().First();
        ScrollBar bar = viewer.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
        ScrollContentPresenter content = viewer.GetVisualDescendants().OfType<ScrollContentPresenter>().First();

        viewer.AllowAutoHide.Should().BeFalse();
        bar.IsVisible.Should().BeTrue("the content is taller than the view");
        bar.IsExpanded.Should().BeTrue("the bar is always shown at its full width");
        content.Bounds.Right.Should().BeLessThanOrEqualTo(bar.Bounds.Left + 0.5, "the bar is beside the content, not over it");
    }

    [AvaloniaFact]
    public void A_scroll_viewer_has_an_expanded_bar_beside_its_content()
    {
        (Window window, ScrollViewer viewer) = Show(new ScrollViewer { Content = new Border { Height = 2000 } });

        ShouldBeExpandedBesideTheContent(viewer);
        window.Close();
    }

    [AvaloniaFact]
    public void A_multi_line_text_box_has_an_expanded_bar_beside_its_text()
    {
        (Window window, TextBox box) = Show(new TextBox
        {
            AcceptsReturn = true,
            Height = 100,
            Text = string.Join("\n", Enumerable.Range(1, 50)),
        });

        ShouldBeExpandedBesideTheContent(box);
        window.Close();
    }

    [AvaloniaFact]
    public void The_code_editor_has_an_expanded_bar_beside_its_text()
    {
        (Window window, AvaloniaEdit.TextEditor editor) = Show(new AvaloniaEdit.TextEditor
        {
            Document = new AvaloniaEdit.Document.TextDocument(string.Join("\n", Enumerable.Range(1, 200))),
        });

        ShouldBeExpandedBesideTheContent(editor);
        window.Close();
    }
}
