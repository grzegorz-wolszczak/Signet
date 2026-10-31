using System.Runtime.ExceptionServices;

namespace NativeWebView.Platform.Linux;

/// <summary>
/// Signet: the exceptions caught in the callbacks WebKitGTK / GLib make into this backend on its GTK thread (signal
/// handlers, actions run on the GTK thread). They are caught because an exception unwinding through the native GTK frames
/// corrupts the GTK thread's stack (see LinuxNativeWebViewBackend.Signet.cs); the process is intact afterwards, so the
/// application can report them instead of ending.
/// </summary>
public static class LinuxNativeCallbackExceptions
{
    // The UI thread's context, taken from the last backend created on a thread that has one (all the backends of an
    // application are created on its UI thread).
    private static SynchronizationContext? s_context;

    /// <summary>
    /// Raised on the UI thread with an exception caught in a native callback. Without a handler the exception is rethrown
    /// on the UI thread (an unhandled exception of the application), or written to stderr when there is no UI thread.
    /// </summary>
    public static event Action<Exception>? Caught;

    internal static void UseContext(SynchronizationContext? context)
    {
        if (context is not null)
            Volatile.Write(ref s_context, context);
    }

    internal static void Report(Exception exception)
    {
        var context = Volatile.Read(ref s_context);
        if (context is null)
        {
            Console.Error.WriteLine($"NativeWebView (Linux): exception in a native callback: {exception}");
            return;
        }

        var captured = ExceptionDispatchInfo.Capture(exception);
        context.Post(static state =>
        {
            var exceptionInfo = (ExceptionDispatchInfo)state!;
            var handler = Caught;
            if (handler is null)
                exceptionInfo.Throw();
            else
                handler(exceptionInfo.SourceException);
        }, captured);
    }
}
