using AutoFixture;
using AwesomeAssertions;
using NativeWebView.Platform.Linux;

namespace Signet.NativeWebView.Platform.Linux.Tests.Fork;

/// <summary>
/// The fork raises the events of the web view on the UI thread: WebKitGTK calls the backend on NativeWebView's GTK
/// thread, and handlers run there used to throw "Call from invalid thread" through the native GTK frames, which ended
/// the process with "stack smashing detected" (UPSTREAM.md, "Events on the UI thread").
/// </summary>
[Collection(ForkStaticStateCollection.Name)]
public sealed class UiThreadEventsTests
{
    // Runs the action on a thread of its own and waits for it. Not Task.Run: xUnit runs a test on a thread of the pool,
    // which goes back to the pool at an await and may then run the very work it waits for — the "other" thread was then
    // the thread the backend was created on, and the event was raised at once (found in November 2026: the first test
    // failed about one run in three). The UI thread of the application never runs work of the pool, so the backend
    // telling threads apart by their id is right; the tests have to use a thread that is surely another one.
    private static void RunOnAnotherThread(Action action)
    {
        var thread = new Thread(() => action()) { IsBackground = true };
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void An_event_raised_on_another_thread_is_posted_to_the_context_the_backend_was_created_on()
    {
        // Arrange
        var uiContext = new RecordingSynchronizationContext();
        using var backend = uiContext.CreateUnder(() => new LinuxNativeWebViewBackend());
        var raisedOnThread = -1;

        // Act
        RunOnAnotherThread(() => backend.RaiseEvent(() => raisedOnThread = Environment.CurrentManagedThreadId));

        // Assert
        raisedOnThread.Should().Be(-1, "the event waits for the UI thread");
        uiContext.PostedCount.Should().Be(1);

        // Act
        uiContext.RunPosted();

        // Assert
        raisedOnThread.Should().Be(Environment.CurrentManagedThreadId, "the test thread plays the UI thread");
    }

    [Fact]
    public void An_event_raised_on_the_thread_the_backend_was_created_on_is_raised_at_once()
    {
        // Arrange
        var uiContext = new RecordingSynchronizationContext();
        using var backend = uiContext.CreateUnder(() => new LinuxNativeWebViewBackend());
        var raised = false;

        // Act
        backend.RaiseEvent(() => raised = true);

        // Assert
        raised.Should().BeTrue();
        uiContext.PostedCount.Should().Be(0);
    }

    [Fact]
    public async Task Without_a_context_an_event_is_raised_at_once_on_any_thread_as_upstream()
    {
        // Arrange
        using var backend = new LinuxNativeWebViewBackend();
        var raisedOnThread = -1;
        var callingThread = -1;

        // Act
        await Task.Run(
            () =>
            {
                callingThread = Environment.CurrentManagedThreadId;
                backend.RaiseEvent(() => raisedOnThread = Environment.CurrentManagedThreadId);
            },
            TestContext.Current.CancellationToken);

        // Assert
        raisedOnThread.Should().Be(callingThread);
    }

    [Fact]
    public void Events_raised_on_another_thread_reach_the_UI_thread_in_the_order_they_were_raised()
    {
        // Arrange
        var uiContext = new RecordingSynchronizationContext();
        using var backend = uiContext.CreateUnder(() => new LinuxNativeWebViewBackend());
        var names = new Fixture().CreateMany<string>(3).ToArray();
        var raised = new List<string>();

        // Act
        RunOnAnotherThread(() =>
        {
            foreach (var name in names)
                backend.RaiseEvent(() => raised.Add(name));
        });

        // Assert
        raised.Should().BeEmpty("the events wait for the UI thread");
        uiContext.PostedCount.Should().Be(names.Length);

        // Act
        uiContext.RunPosted();

        // Assert
        raised.Should().Equal(names);
    }
}
