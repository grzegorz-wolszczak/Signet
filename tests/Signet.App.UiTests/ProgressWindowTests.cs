using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AwesomeAssertions;
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
        Window owner = Owner();

        Task<string?> run = ProgressWindow.RunAsync(owner, "Title", "Working", Task.FromResult<string?>("done"));
        RunUntil(() => run.IsCompleted);

        run.Result.Should().Be("done");
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void The_window_shows_while_the_work_runs_and_closes_with_its_result()
    {
        Window owner = Owner();
        TaskCompletionSource<string?> work = new();

        Task<string?> run = ProgressWindow.RunAsync(owner, "Title", "Working", work.Task, TimeSpan.Zero);
        RunUntil(() => owner.OwnedWindows.Count == 1);
        ProgressWindow window = owner.OwnedWindows.OfType<ProgressWindow>().Single();
        window.Title.Should().Be("Title");
        window.FindControl<TextBlock>("MessageText")!.Text.Should().Be("Working");

        work.SetResult("done");
        RunUntil(() => run.IsCompleted);

        run.Result.Should().Be("done");
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void Cancel_drops_the_result_of_the_work()
    {
        Window owner = Owner();
        TaskCompletionSource<string?> work = new();

        Task<string?> run = ProgressWindow.RunAsync(owner, "Title", "Working", work.Task, TimeSpan.Zero);
        RunUntil(() => owner.OwnedWindows.Count == 1);
        owner.OwnedWindows.OfType<ProgressWindow>().Single().FindControl<Button>("CancelButton")!
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        RunUntil(() => run.IsCompleted);
        work.SetException(new InvalidOperationException("not wanted any more"));
        Dispatcher.UIThread.RunJobs();

        run.Result.Should().BeNull();
        owner.Close();
    }

    [AvaloniaFact]
    public void While_the_work_runs_the_window_has_no_cancel_and_cannot_be_closed()
    {
        Window owner = Owner();
        TaskCompletionSource work = new();

        Task run = ProgressWindow.ShowWhileAsync(owner, "Title", "Loading", work.Task, TimeSpan.Zero);
        RunUntil(() => owner.OwnedWindows.Count == 1);
        ProgressWindow window = owner.OwnedWindows.OfType<ProgressWindow>().Single();
        window.FindControl<Button>("CancelButton")!.IsVisible.Should().BeFalse();
        window.Close();
        Dispatcher.UIThread.RunJobs();
        owner.OwnedWindows.Should().ContainSingle("only the end of the work closes it");

        work.SetResult();
        RunUntil(() => run.IsCompleted);

        run.IsCompletedSuccessfully.Should().BeTrue();
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }

    [AvaloniaFact]
    public void An_exception_of_the_work_comes_out_after_the_window()
    {
        Window owner = Owner();
        TaskCompletionSource work = new();

        Task run = ProgressWindow.ShowWhileAsync(owner, "Title", "Loading", work.Task, TimeSpan.Zero);
        RunUntil(() => owner.OwnedWindows.Count == 1);
        work.SetException(new InvalidOperationException("broken"));
        RunUntil(() => run.IsCompleted);

        run.Exception!.InnerException.Should().BeOfType<InvalidOperationException>();
        owner.OwnedWindows.Should().BeEmpty();
        owner.Close();
    }
}
