using AutoFixture;
using AwesomeAssertions;
using NativeWebView.Platform.Linux;

namespace Signet.NativeWebView.Platform.Linux.Tests.Fork;

/// <summary>
/// The fork lets no managed exception out of a callback WebKitGTK / GLib makes on the GTK thread: one unwinding through
/// the native GTK frames corrupts the thread's stack ("stack smashing detected"). A caught exception goes to the UI thread
/// (UPSTREAM.md, "No managed exception through the native frames").
/// </summary>
[Collection(ForkStaticStateCollection.Name)]
public sealed class NativeCallbackExceptionsTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public void An_exception_in_a_signal_handler_does_not_leave_it_and_reaches_the_Caught_event_on_the_UI_thread()
    {
        // Arrange
        var uiContext = new RecordingSynchronizationContext();
        using var backend = uiContext.CreateUnder(() => new LinuxNativeWebViewBackend());
        var thrown = new InvalidOperationException(_fixture.Create<string>());
        Exception? caught = null;
        void OnCaught(Exception exception) => caught = exception;
        LinuxNativeCallbackExceptions.Caught += OnCaught;
        try
        {
            // Act
            var act = () => backend.GuardCallback(() => throw thrown);

            // Assert
            act.Should().NotThrow();
            caught.Should().BeNull("the handler runs on the UI thread");

            // Act
            uiContext.RunPosted();

            // Assert
            caught.Should().BeSameAs(thrown);
        }
        finally
        {
            LinuxNativeCallbackExceptions.Caught -= OnCaught;
        }
    }

    [Fact]
    public void A_signal_handler_that_throws_returns_its_fallback_value_to_GTK()
    {
        // Arrange
        var uiContext = new RecordingSynchronizationContext();
        using var backend = uiContext.CreateUnder(() => new LinuxNativeWebViewBackend());
        var fallback = _fixture.Create<int>();

        // Act
        var result = backend.GuardCallback(() => throw new InvalidOperationException(), fallback);

        // Assert
        result.Should().Be(fallback);
    }

    [Fact]
    public void Without_a_Caught_handler_the_exception_is_rethrown_on_the_UI_thread()
    {
        // Arrange
        var uiContext = new RecordingSynchronizationContext();
        using var backend = uiContext.CreateUnder(() => new LinuxNativeWebViewBackend());
        var thrown = new InvalidOperationException(_fixture.Create<string>());
        backend.GuardCallback(() => throw thrown);

        // Act & Assert
        uiContext.Invoking(c => c.RunPosted()).Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(thrown);
    }
}
