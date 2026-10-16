using System;
using System.Collections.Generic;
using System.Linq;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.Services;

/// <summary>
/// What the report window of a file-by-file operation shows: the operation, how many files it went through and the
/// files it skipped (with the reason and the position of the problem).
/// </summary>
/// <param name="OperationName">Translated name of the operation.</param>
/// <param name="Processed">The number of files processed without a problem.</param>
/// <param name="Skipped">The skipped files.</param>
public sealed record SkippedFilesReport(string OperationName, int Processed, IReadOnlyList<SkippedFile> Skipped)
{
    /// <summary>The header of the report window: the operation and the counts.</summary>
    public string Header => Strings.Format("SkippedFilesDialog_Header", OperationName, Processed, Skipped.Count);

    /// <summary>The report as the warnings tree of the shared "warnings" window: one group with a file per skipped file.</summary>
    public IReadOnlyList<LoadWarning> ToWarnings() => new[]
    {
        new LoadWarning(
            Strings.Format("SkippedFilesDialog_GroupMessage", string.Join(", ", Skipped.Select(s => s.BookPath))),
            Strings.Get("SkippedFilesDialog_Group"),
            Skipped.Select(s => new LoadWarningItem(s.BookPath, s.Reason, s.Line, s.Column)).ToList()),
    };
}

/// <summary>
/// Reports the result of an operation that is applied file by file (<see cref="FileBatchResult"/>): the status bar
/// only says how many files went through and how many were skipped; the details are for the report window.
/// </summary>
public static class FileBatchReporter
{
    /// <summary>
    /// Shows the outcome in the status bar: <paramref name="successMessage"/> when nothing was skipped, otherwise the
    /// numbers of processed and skipped files as a warning.
    /// </summary>
    /// <returns>The report for the report window, or <c>null</c> when no file was skipped.</returns>
    public static SkippedFilesReport? ShowStatus(
        IStatusBarService statusBar, string operationName, FileBatchResult result, string successMessage)
    {
        ArgumentNullException.ThrowIfNull(statusBar);
        ArgumentNullException.ThrowIfNull(operationName);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(successMessage);

        if (!result.HasSkipped)
        {
            statusBar.ShowMessage(successMessage, TimeSpan.FromSeconds(4));
            return null;
        }

        string text = result.NothingProcessed
            ? Strings.Format("Status_BatchNothingProcessed", operationName, result.Skipped.Count)
            : Strings.Format("Status_BatchPartial", operationName, result.Processed, result.Skipped.Count);
        statusBar.ShowMessage(text, TimeSpan.FromSeconds(6), NotificationLevel.Warning);
        return new SkippedFilesReport(operationName, result.Processed, result.Skipped);
    }
}
