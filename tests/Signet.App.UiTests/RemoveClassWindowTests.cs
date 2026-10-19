using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.Resources;
using Signet.App.Views;
using Signet.Core.BookManipulation;

namespace Signet.App.UiTests;

/// <summary>Rendering of the "Remove Class" confirmation window.</summary>
public sealed class RemoveClassWindowTests
{
    [AvaloniaFact]
    public void The_window_shows_the_intro_and_the_usages_per_file_and_cancels_by_default()
    {
        // Arrange
        ClassUsageFileSummary[] summary =
        {
            new("text/ch1.xhtml", 2),
            new("styles/a.css", 1),
        };
        Window owner = new();

        // Act
        owner.Show();

        _ = RemoveClassWindow.AskAsync(owner, "note", 3, summary);
        Dispatcher.UIThread.RunJobs();
        RemoveClassWindow window = owner.OwnedWindows.OfType<RemoveClassWindow>().Single();
        window.CaptureRenderedFrame();

        List<string?> texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

        // Assert
        texts.Should().Contain(Strings.Format("RemoveClassWindow_Intro", "note", 3, 2));
        texts.Should().Contain("text/ch1.xhtml");
        texts.Should().Contain("2");
        texts.Should().Contain("styles/a.css");
        texts.Should().Contain("1");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton").IsDefault.Should().BeTrue();
        window.Close();
        owner.Close();
    }

    [AvaloniaFact]
    public void Accept_closes_with_true_and_cancel_closes_with_false()
    {
        // Arrange
        Window owner = new();
        owner.Show();

        // Act
        var acceptTask = RemoveClassWindow.AskAsync(owner, "note", 1, new[] { new ClassUsageFileSummary("a.xhtml", 1) });
        Dispatcher.UIThread.RunJobs();
        RemoveClassWindow acceptWindow = owner.OwnedWindows.OfType<RemoveClassWindow>().Single();
        acceptWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AcceptButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Assert
        acceptTask.IsCompletedSuccessfully.Should().BeTrue();
        acceptTask.Result.Should().BeTrue();

        // Act
        var cancelTask = RemoveClassWindow.AskAsync(owner, "note", 1, new[] { new ClassUsageFileSummary("a.xhtml", 1) });
        Dispatcher.UIThread.RunJobs();
        RemoveClassWindow cancelWindow = owner.OwnedWindows.OfType<RemoveClassWindow>().Single();
        cancelWindow.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Assert
        cancelTask.IsCompletedSuccessfully.Should().BeTrue();
        cancelTask.Result.Should().BeFalse();
        owner.Close();
    }
}
