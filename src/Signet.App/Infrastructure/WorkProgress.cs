using System;

namespace Signet.App.Infrastructure;

/// <summary>
/// Where a long operation is, as the progress window shows it (<c>ProgressWindow.RunWithProgressAsync</c>): what it is doing
/// and how far it is.
/// </summary>
/// <param name="Message">
/// What it is doing now ("Converting footnotes: 120 of 7405"); <c>null</c> keeps the last message. In a report with a
/// <paramref name="Phase"/> it is the detail of the phase ("footnote 120 of 7405"), and <c>null</c> means there is none.
/// </param>
/// <param name="Fraction">
/// How far it is, from 0 to 1; <c>null</c> when it cannot tell (the bar then only shows that it works). In a report with a
/// <paramref name="Phase"/> it is the progress of the deepest level the report gives: of the detail when it counts steps, else of
/// the phase itself (it starts over with each phase).
/// </param>
/// <param name="Phase">The phase of an operation made of phases ("Phase 2 of 5: Numbering"); <c>null</c> when it has none.</param>
public sealed record WorkProgress(string? Message, double? Fraction, PhaseInfo? Phase = null)
{
    /// <summary>The progress of <paramref name="done"/> of <paramref name="total"/> steps, as a fraction of the operation.</summary>
    public static double FractionOf(int done, int total) => total <= 0 ? 0 : Math.Clamp((double)done / total, 0, 1);

    /// <summary>
    /// A report of a phase that counts its steps: the bar is at <paramref name="done"/> of <paramref name="total"/> of the phase
    /// (it starts over with each phase) and <paramref name="detail"/> says which step. While the count is not known
    /// (<paramref name="total"/> is 0) there is neither a detail nor a percent: the bar only shows that the phase works.
    /// </summary>
    public static WorkProgress InPhase(PhaseInfo phase, string? detail, int done, int total) =>
        total <= 0 ? new WorkProgress(null, null, phase) : new WorkProgress(detail, FractionOf(done, total), phase);

    /// <summary>
    /// A report of an operation made of stages, each with a share of the whole: the bar is at <paramref name="start"/> plus
    /// <paramref name="done"/> of <paramref name="total"/> of the share <paramref name="share"/>.
    /// </summary>
    public static WorkProgress InStage(string? message, double start, double share, int done, int total) =>
        new(message, Math.Clamp(start + (share * FractionOf(done, total)), 0, 1));
}

/// <summary>The phase an operation made of phases is in, as the progress window shows it ("Phase 2 of 5: Numbering").</summary>
/// <param name="Index">The number of the phase, from 1.</param>
/// <param name="Count">How many phases the operation has.</param>
/// <param name="Name">What the phase does ("Numbering").</param>
public sealed record PhaseInfo(int Index, int Count, string Name);

/// <summary>
/// Runs a long operation with a progress window over the owner of the operation: the view model calls it with the work, which
/// reports its progress; a runner without a window (the tests) just runs the work.
/// </summary>
/// <param name="title">The name of the operation (the title of the window).</param>
/// <param name="work">
/// The work; it reports to the progress it is given from any thread and stops with an
/// <see cref="OperationCanceledException"/> when the token it is given is cancelled (the user pressed Cancel).
/// </param>
/// <param name="cancellable">
/// Whether the window has a Cancel button: only an operation that can be brought back to the state before it (a checkpoint
/// was taken) can be cancelled.
/// </param>
public delegate System.Threading.Tasks.Task ProgressRunner(
    string title, Func<IProgress<WorkProgress>, System.Threading.CancellationToken, System.Threading.Tasks.Task> work, bool cancellable);
