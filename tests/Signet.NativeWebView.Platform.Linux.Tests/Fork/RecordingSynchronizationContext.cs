namespace Signet.NativeWebView.Platform.Linux.Tests.Fork;

/// <summary>
/// A synchronization context standing for the UI thread: it keeps what is posted to it, and runs it only when the test
/// asks (<see cref="RunPosted"/>).
/// </summary>
internal sealed class RecordingSynchronizationContext : SynchronizationContext
{
    private readonly List<(SendOrPostCallback Callback, object? State)> _posted = [];

    /// <summary>How many callbacks were posted and not run yet.</summary>
    public int PostedCount
    {
        get
        {
            lock (_posted)
            {
                return _posted.Count;
            }
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_posted)
        {
            _posted.Add((d, state));
        }
    }

    /// <summary>Runs the posted callbacks, in the order they were posted.</summary>
    public void RunPosted()
    {
        (SendOrPostCallback Callback, object? State)[] posted;
        lock (_posted)
        {
            posted = [.. _posted];
            _posted.Clear();
        }

        foreach (var (callback, state) in posted)
        {
            callback(state);
        }
    }

    /// <summary>Runs <paramref name="create"/> with this context as the current one (as the UI thread would).</summary>
    public T CreateUnder<T>(Func<T> create)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            return create();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}
