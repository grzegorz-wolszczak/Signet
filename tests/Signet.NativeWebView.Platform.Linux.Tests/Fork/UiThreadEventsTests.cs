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
    [Fact]
    public async Task An_event_raised_on_another_thread_is_posted_to_the_context_the_backend_was_created_on()
    {
        // Arrange
        var uiContext = new RecordingSynchronizationContext();
        using var backend = uiContext.CreateUnder(() => new LinuxNativeWebViewBackend());
        var raisedOnThread = -1;

        // Act
        await Task.Run(
            () => backend.RaiseEvent(() => raisedOnThread = Environment.CurrentManagedThreadId),
            TestContext.Current.CancellationToken);

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
    public async Task Events_raised_on_another_thread_reach_the_UI_thread_in_the_order_they_were_raised()
    {
        // Arrange
        var uiContext = new RecordingSynchronizationContext();
        using var backend = uiContext.CreateUnder(() => new LinuxNativeWebViewBackend());
        var names = new Fixture().CreateMany<string>(3).ToArray();
        var raised = new List<string>();

        // Act
        await Task.Run(
            () =>
            {
                foreach (var name in names)
                    backend.RaiseEvent(() => raised.Add(name));
            },
            TestContext.Current.CancellationToken);
        uiContext.RunPosted();

        // Assert
        raised.Should().Equal(names);
    }
}
