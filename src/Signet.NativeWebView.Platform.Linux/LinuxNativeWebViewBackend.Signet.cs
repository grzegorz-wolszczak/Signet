namespace NativeWebView.Platform.Linux;

// Signet: the backend's events on the UI thread, and no managed exception through the native GTK frames. See
// UPSTREAM.md ("Events on the UI thread").
//
// WebKitGTK calls the signal handlers of this backend on NativeWebView's own GTK thread ("NativeWebView.GTK",
// LinuxGtkDispatcher), inside gtk_main_iteration. Upstream raises the backend's events (NavigationStarted,
// NavigationCompleted, WebMessageReceived, ...) right there, and neither NativeWebView.Core nor the NativeWebView control
// moves them to the UI thread - so on Linux the application's handlers ran on the GTK thread, unlike on Windows, where
// WebView2 raises them on the UI thread. A handler touching an Avalonia control threw "Call from invalid thread"; the
// exception then unwound through the native GTK/GLib frames between the signal and gtk_main_iteration, which corrupts the
// stack: the process died with "*** stack smashing detected ***" (seen in Signet on "Close All Tabs", which clears the
// preview, navigates and runs scripts at once - so it did not happen every time).
//
// Here: the events raised from the GTK signals are posted to the SynchronizationContext the backend was created on (the
// NativeWebView control creates it on the UI thread); the policy decisions the events answer (NavigationStarted.Cancel,
// NewWindowRequested.Handled) are kept by WebKit until the UI thread has decided; and every signal handler catches what it
// throws and hands it to the UI thread (LinuxNativeCallbackExceptions) instead of letting it into the native frames. Without a SynchronizationContext
// (unit tests, a backend created off any UI thread) everything stays as upstream: raised on the calling thread.
public sealed partial class LinuxNativeWebViewBackend
{
    // The thread the events go to, and its context: the ones the backend was created on.
    private readonly SynchronizationContext? _eventContext = CaptureEventContext();
    private readonly int _eventThreadId = Environment.CurrentManagedThreadId;

    private static SynchronizationContext? CaptureEventContext()
    {
        var context = SynchronizationContext.Current;
        LinuxNativeCallbackExceptions.UseContext(context);
        return context;
    }

    // Whether an event raised on the current thread has to be posted to the event thread (it is raised directly otherwise).
    // The thread is compared, not the context: Avalonia runs its dispatcher operations under several context instances.
    internal bool RaisesOnOtherThread => _eventContext is not null && Environment.CurrentManagedThreadId != _eventThreadId;

    // Raises an event on the event thread: directly when already there (or without a context), posted otherwise. Posting
    // keeps the order of the events raised from the GTK thread. An exception of a handler surfaces on the event thread,
    // as it would on the other platforms.
    internal void RaiseEvent(Action raise)
    {
        if (!RaisesOnOtherThread)
        {
            raise();
            return;
        }

        _eventContext!.Post(static state => ((Action)state!).Invoke(), raise);
    }

    // Answers a WebKit policy decision on the event thread: WebKit keeps the decision (a reference is taken here) until
    // webkit_policy_decision_use/ignore is called, so the handler on the UI thread can still cancel the navigation.
    // shouldIgnore raises the event and returns whether to ignore the request. Returns the value of the "decide-policy"
    // signal: 1 - the decision is handled (later).
    private int DecideOnEventThread(IntPtr decision, Func<bool> shouldIgnore)
    {
        LinuxNativeInterop.g_object_ref(decision);
        RaiseEvent(() =>
        {
            var ignore = false;
            try
            {
                ignore = shouldIgnore();
            }
            finally
            {
                // Even when a handler throws, WebKit gets its answer (the default: use) and the reference is released.
                LinuxNativeInterop.EnqueueOnGtkThread(() =>
                {
                    try
                    {
                        if (ignore)
                            LinuxNativeInterop.webkit_policy_decision_ignore(decision);
                        else
                            LinuxNativeInterop.webkit_policy_decision_use(decision);
                    }
                    finally
                    {
                        LinuxNativeInterop.g_object_unref(decision);
                    }
                });
            }
        });
        return 1;
    }

    // The body of a GTK signal handler: an exception must not leave it (it would unwind through the native GTK frames).
    internal void GuardCallback(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            ReportCallbackException(exception);
        }
    }

    // The body of a GTK signal handler that returns a value; fallback is returned when it throws.
    internal int GuardCallback(Func<int> callback, int fallback)
    {
        try
        {
            return callback();
        }
        catch (Exception exception)
        {
            ReportCallbackException(exception);
            return fallback;
        }
    }

    // An exception caught in a signal handler goes to the UI thread: to LinuxNativeCallbackExceptions.Caught (Signet logs
    // it and shows a notification), or rethrown there as an unhandled exception when nobody handles it.
    private static void ReportCallbackException(Exception exception) => LinuxNativeCallbackExceptions.Report(exception);
}
