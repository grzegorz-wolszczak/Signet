using System;
using System.Threading.Tasks;

namespace Signet.App.ViewModels;

/// <summary>The outcome of a book operation (<see cref="IBookOperationHost.RunAsync{T}"/>).</summary>
/// <param name="Completed">Whether the work ran to the end (not cancelled, not refused).</param>
/// <param name="Result">What the work returned; <c>default</c> when it did not complete.</param>
/// <param name="Checkpoint">Whether a checkpoint was taken before the operation (and, when cancelled, used to bring the book back).</param>
public readonly record struct BookOperationOutcome<T>(bool Completed, T? Result, bool Checkpoint);

/// <summary>
/// Runs a long operation on the whole book for a view model that does not own the book operations (the Book Browser): the
/// checkpoint is taken and the work runs off the UI thread under a progress window, with every other operation on the book off
/// meanwhile. Implemented by the main window's view model.
/// </summary>
public interface IBookOperationHost
{
    /// <summary>
    /// Runs <paramref name="work"/>, which edits the book, as a book operation headed <paramref name="title"/>: behind the
    /// checkpoint <paramref name="checkpointName"/> (none when <c>null</c>), off the UI thread, under a progress window. Taking the
    /// checkpoint can be cancelled (nothing happens then); the work itself cannot (it does not check a token). An exception of
    /// the work is thrown here — a work that wants the checkpoint rewound catches its own exceptions and returns them.
    /// </summary>
    Task<BookOperationOutcome<T>> RunAsync<T>(string title, string? checkpointName, Func<T> work);
}
