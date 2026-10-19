using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
using Signet.App.Infrastructure;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>Tests of the window shown while work runs in the background (<see cref="ProgressWindow"/>).</summary>
public sealed class ProgressWindowTests
{
    private static Window Owner()
    {
        Window owner = new();
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        return owner;
    }

    // Runs the jobs of the UI thread until the condition holds (the delay and the continuations need a few rounds).
    private static void RunUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(5);
        }

        condition().Should().BeTrue();
    }

    [AvaloniaFact]
    public void Work_done_at_once_gives_its_result_without_a_window()
    {
        // Arrange
        Window owner = Owner();

        // Act
        Task<string?> run = ProgressWindow.RunAsync(owner, "Title", "Working", Task.FromResult<string?>("done"));
        RunUntil(() => run.IsCompleted);

        // Assert
        run.Result.Should().Be("done");
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void The_window_shows_while_the_work_runs_and_closes_with_its_result()
    {
        // Arrange
        Window owner = Owner();
        TaskCompletionSource<string?> work = new();

        // Act
        Task<string?> run = ProgressWindow.RunAsync(owner, "Title", "Working", work.Task, TimeSpan.Zero);
        RunUntil(() => owner.OwnedWindows.Count == 1);
        ProgressWindow window = owner.OwnedWindows.OfType<ProgressWindow>().Single();

        // Assert
        window.Title.Should().Be("Title");
        window.FindControl<TextBlock>("MessageText")!.Text.Should().Be("Working");

        // Act
        work.SetResult("done");
        RunUntil(() => run.IsCompleted);

        // Assert
        run.Result.Should().Be("done");
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void Cancel_drops_the_result_of_the_work()
    {
        // Arrange
        Window owner = Owner();
        TaskCompletionSource<string?> work = new();

        // Act
        Task<string?> run = ProgressWindow.RunAsync(owner, "Title", "Working", work.Task, TimeSpan.Zero);
        RunUntil(() => owner.OwnedWindows.Count == 1);
        owner.OwnedWindows.OfType<ProgressWindow>().Single().FindControl<Button>("CancelButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        RunUntil(() => run.IsCompleted);
        work.SetException(new InvalidOperationException("not wanted any more"));
        Dispatcher.UIThread.RunJobs();

        // Assert
        run.Result.Should().BeNull();
        owner.Close();
    }

    [AvaloniaFact]
    public void While_the_work_runs_the_window_has_no_cancel_and_cannot_be_closed()
    {
        // Arrange
        Window owner = Owner();
        TaskCompletionSource work = new();

        // Act
        Task run = ProgressWindow.ShowWhileAsync(owner, "Title", "Loading", work.Task, TimeSpan.Zero);
        RunUntil(() => owner.OwnedWindows.Count == 1);
        ProgressWindow window = owner.OwnedWindows.OfType<ProgressWindow>().Single();

        // Assert
        window.FindControl<Button>("CancelButton")!.IsVisible.Should().BeFalse();

        // Act
        window.Close();
        Dispatcher.UIThread.RunJobs();

        // Assert
        owner.OwnedWindows.Should().ContainSingle("only the end of the work closes it");

        // Act
        work.SetResult();
        RunUntil(() => run.IsCompleted);

        // Assert
        run.IsCompletedSuccessfully.Should().BeTrue();
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void An_exception_of_the_work_comes_out_after_the_window()
    {
        // Arrange
        Window owner = Owner();
        TaskCompletionSource work = new();

        // Act
        Task run = ProgressWindow.ShowWhileAsync(owner, "Title", "Loading", work.Task, TimeSpan.Zero);
        RunUntil(() => owner.OwnedWindows.Count == 1);
        work.SetException(new InvalidOperationException("broken"));
        RunUntil(() => run.IsCompleted);

        // Assert
        run.Exception!.InnerException.Should().BeOfType<InvalidOperationException>();
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void The_window_shows_the_message_and_the_percent_the_work_reports_and_closes_when_it_is_done()
    {
        // Arrange
        Window owner = Owner();
        TaskCompletionSource work = new();
        IProgress<WorkProgress>? progress = null;

        // Act
        Task run = ProgressWindow.RunWithProgressAsync(
            owner, "Converting", "Starting", (p, _) =>
            {
                progress = p;
                return work.Task;
            });
        RunUntil(() => owner.OwnedWindows.Count == 1);
        ProgressWindow window = owner.OwnedWindows.OfType<ProgressWindow>().Single();
        ProgressBar bar = window.FindControl<ProgressBar>("Bar")!;
        TextBlock percent = window.FindControl<TextBlock>("PercentText")!;

        // Assert
        window.Title.Should().Be("Converting");
        window.FindControl<TextBlock>("MessageText")!.Text.Should().Be("Starting");
        bar.IsIndeterminate.Should().BeTrue("the work has not said how far it is");
        percent.IsVisible.Should().BeFalse();
        window.FindControl<Button>("CancelButton")!.IsVisible.Should().BeFalse();

        // Act
        progress!.Report(new WorkProgress("Converting: 3 of 4", 0.75));
        RunUntil(() => bar.Value == 75);

        // Assert
        window.FindControl<TextBlock>("MessageText")!.Text.Should().Be("Converting: 3 of 4");
        bar.IsIndeterminate.Should().BeFalse();
        percent.IsVisible.Should().BeTrue();
        percent.Text.Should().Contain("75");

        // Act
        progress.Report(new WorkProgress(null, null));
        RunUntil(() => bar.IsIndeterminate);

        // Assert
        window.FindControl<TextBlock>("MessageText")!.Text.Should().Be("Converting: 3 of 4", "a report without a message keeps the last one");
        percent.IsVisible.Should().BeFalse();

        // Act
        work.SetResult();
        RunUntil(() => run.IsCompleted);

        // Assert
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void Work_that_is_done_before_the_delay_shows_no_window()
    {
        // Arrange
        Window owner = Owner();

        // Act
        Task run = ProgressWindow.RunWithProgressAsync(owner, "Title", "Working", (_, _) => Task.CompletedTask);
        RunUntil(() => run.IsCompleted);

        // Assert
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void An_exception_of_the_work_is_thrown_to_the_caller()
    {
        // Arrange
        Window owner = Owner();

        // Act
        Task run = ProgressWindow.RunWithProgressAsync(owner, "Title", "Working", (_, _) => Task.FromException(new InvalidOperationException("boom")));
        RunUntil(() => run.IsCompleted);

        // Assert
        run.IsFaulted.Should().BeTrue();
        run.Exception!.InnerException.Should().BeOfType<InvalidOperationException>();
        owner.Close();
    }

    [AvaloniaFact]
    public void A_cancellable_window_cancels_the_token_says_so_and_stays_until_the_work_has_stopped()
    {
        // Arrange
        Window owner = Owner();
        TaskCompletionSource work = new();
        CancellationToken token = default;

        // Act
        Task run = ProgressWindow.RunWithProgressAsync(
            owner, "Converting", "Working", (_, t) =>
            {
                token = t;
                return work.Task;
            }, cancellable: true);
        RunUntil(() => owner.OwnedWindows.Count == 1);
        ProgressWindow window = owner.OwnedWindows.OfType<ProgressWindow>().Single();
        Button cancel = window.FindControl<Button>("CancelButton")!;

        // Assert
        cancel.IsVisible.Should().BeTrue();
        token.IsCancellationRequested.Should().BeFalse();

        // Act
        cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Assert
        token.IsCancellationRequested.Should().BeTrue();
        cancel.IsEnabled.Should().BeFalse();
        window.FindControl<TextBlock>("MessageText")!.Text.Should().Be(Signet.App.Resources.Strings.Get("ProgressWindow_Cancelling"));
        owner.OwnedWindows.Should().ContainSingle("the work has not stopped yet");

        // Act
        work.SetCanceled(token);
        RunUntil(() => run.IsCompleted);

        // Assert
        run.IsCanceled.Should().BeTrue();
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void A_window_that_is_not_cancellable_has_no_cancel_button()
    {
        // Arrange
        Window owner = Owner();
        TaskCompletionSource work = new();

        // Act
        Task run = ProgressWindow.RunWithProgressAsync(owner, "Converting", "Working", (_, _) => work.Task);
        RunUntil(() => owner.OwnedWindows.Count == 1);

        // Assert
        owner.OwnedWindows.OfType<ProgressWindow>().Single().FindControl<Button>("CancelButton")!.IsVisible.Should().BeFalse();

        // Act
        work.SetResult();
        RunUntil(() => run.IsCompleted);
        owner.Close();
    }
}
