using System;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Signet.App.Infrastructure;

/// <summary>
/// Coalesces frequent calls to <see cref="Report"/> into at most one <c>apply</c> per <c>minInterval</c> of
/// wall-clock time (the constructor parameter): a call right after a quiet spell — including the very first call
/// ever — goes through at once, so a short operation with only a few reports is never held up waiting for a window
/// that never fills. A burst of calls (e.g. one per file while saving a book of thousands) is coalesced to the
/// latest value and flushed <c>minInterval</c> after the previous flush, so continuous activity cannot starve it and
/// the latest value is never silently dropped — only ever superseded by a newer one before its flush. Disposing
/// flushes a still-pending value first, so the final state of a finished operation is never lost.
/// </summary>
/// <remarks>
/// Built on Reactive Extensions scheduling: unless a <c>scheduler</c> is given (tests), <see cref="Scheduler.Default"/>
/// runs it, a background scheduler — <c>apply</c> does not run on the caller's thread and must marshal to the UI
/// thread itself when it touches UI state.
/// </remarks>
public sealed class ProgressDebouncer<T> : IDisposable
{
    private readonly Subject<(T Value, bool FlushNow)> _reports = new();
    private readonly IDisposable _subscription;

    /// <param name="minInterval">The minimum time between two applied values.</param>
    /// <param name="apply">Called with the coalesced value, on <paramref name="scheduler"/>'s thread.</param>
    /// <param name="scheduler">Where the coalescing timers run; <c>null</c> = <see cref="Scheduler.Default"/> (a background
    /// scheduler using real wall-clock time). Tests pass a virtual-time scheduler to make <paramref name="minInterval"/>
    /// deterministic instead of waiting on it.</param>
    public ProgressDebouncer(TimeSpan minInterval, Action<T> apply, IScheduler? scheduler = null)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _subscription = Coalesce(_reports, minInterval, scheduler ?? Scheduler.Default).Subscribe(apply);
    }

    /// <summary>
    /// Reports a new value; coalesced with any other reported within <c>minInterval</c> of the previous flush, unless
    /// <paramref name="flushNow"/> says this one must not be: a value coalescing can only ever supersede, never skip
    /// past, so a caller whose values are not drop-safe (e.g. a transition to a new phase, which must be seen even if
    /// it is instantly superseded moments later by the first value of that phase) marks it instead of hoping the
    /// timing works out.
    /// </summary>
    public void Report(T value, bool flushNow = false) => _reports.OnNext((value, flushNow));

    /// <summary>Flushes a value still pending (if any), then stops coalescing. Reporting after this has no effect.</summary>
    public void Dispose()
    {
        _reports.OnCompleted();
        _subscription.Dispose();
        _reports.Dispose();
    }

    // At most one value per `minInterval`: the first value after a quiet spell (or ever) goes through at once; while
    // flushes keep landing less than `minInterval` apart, later values replace the pending one and the flush stays
    // scheduled for `minInterval` after the previous flush — a fixed cadence, not reset by each new value, so a
    // continuous stream of reports cannot push the next flush out indefinitely. A value reported with `flushNow` goes
    // through at once regardless, so a transition is never coalesced away entirely. Completion (the source's or a
    // disposal's) flushes a value still pending instead of dropping it.
    private static IObservable<T> Coalesce(IObservable<(T Value, bool FlushNow)> source, TimeSpan minInterval, IScheduler scheduler) =>
        Observable.Create<T>(observer =>
        {
            bool hasPending = false;
            T pending = default!;
            DateTimeOffset? lastFlush = null;
            SerialDisposable scheduledFlush = new();

            void Flush(T value)
            {
                scheduledFlush.Disposable = Disposable.Empty;
                hasPending = false;
                lastFlush = scheduler.Now;
                observer.OnNext(value);
            }

            IDisposable subscription = source.Subscribe(
                report =>
                {
                    if (report.FlushNow || lastFlush is not { } since || scheduler.Now - since >= minInterval)
                    {
                        Flush(report.Value);
                    }
                    else
                    {
                        pending = report.Value;
                        hasPending = true;
                        TimeSpan dueIn = minInterval - (scheduler.Now - since);
                        scheduledFlush.Disposable = scheduler.Schedule(dueIn, () =>
                        {
                            if (hasPending)
                            {
                                Flush(pending);
                            }
                        });
                    }
                },
                observer.OnError,
                () =>
                {
                    if (hasPending)
                    {
                        Flush(pending);
                    }

                    observer.OnCompleted();
                });

            return new CompositeDisposable(subscription, scheduledFlush);
        });
}
