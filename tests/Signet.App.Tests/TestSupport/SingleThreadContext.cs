using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Signet.App.Tests.TestSupport;

/// <summary>
/// Runs an async test body on one thread with a synchronization context, like the UI thread of the application: what follows
/// an <c>await</c> comes back to that thread. xUnit has no such context, so a continuation lands on a thread of the pool, where
/// the editor documents of open tabs (they belong to the thread that made them) cannot be used.
/// </summary>
public static class SingleThreadContext
{
    /// <summary>Runs <paramref name="body"/> on its own thread; the task is done when the body is (an exception of it is thrown).</summary>
    public static Task RunAsync(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            using Context context = new();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Task task = body();
                _ = task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
                context.RunLoop();
                task.GetAwaiter().GetResult();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return done.Task;
    }

    private sealed class Context : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void Complete() => _queue.CompleteAdding();

        public void Dispose() => _queue.Dispose();

        public void RunLoop()
        {
            foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }
    }
}
