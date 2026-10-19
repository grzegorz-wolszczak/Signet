using System;

namespace Signet.App.Infrastructure;

/// <summary>
/// Where a long operation is, as the progress window shows it (<c>ProgressWindow.RunWithProgressAsync</c>): what it is doing
/// and how far it is.
/// </summary>
/// <param name="Message">What it is doing now ("Converting footnotes: 120 of 7405"); <c>null</c> keeps the last message.</param>
/// <param name="Fraction">How far it is, from 0 to 1; <c>null</c> when it cannot tell (the bar then only shows that it works).</param>
public sealed record WorkProgress(string? Message, double? Fraction)
{
    /// <summary>The progress of <paramref name="done"/> of <paramref name="total"/> steps, as a fraction of the operation.</summary>
    public static double FractionOf(int done, int total) => total <= 0 ? 0 : Math.Clamp((double)done / total, 0, 1);

    /// <summary>
    /// A report of an operation made of stages, each with a share of the whole: the bar is at <paramref name="start"/> plus
    /// <paramref name="done"/> of <paramref name="total"/> of the share <paramref name="share"/>.
    /// </summary>
    public static WorkProgress InStage(string? message, double start, double share, int done, int total) =>
        new(message, Math.Clamp(start + (share * FractionOf(done, total)), 0, 1));
}

/// <summary>
/// Runs a long operation with a progress window over the owner of the operation: the view model calls it with the work, which
/// reports its progress; a runner without a window (the tests) just runs the work.
/// </summary>
/// <param name="work">The work; it reports to the progress it is given from any thread.</param>
public delegate System.Threading.Tasks.Task ProgressRunner(Func<IProgress<WorkProgress>, System.Threading.Tasks.Task> work);
