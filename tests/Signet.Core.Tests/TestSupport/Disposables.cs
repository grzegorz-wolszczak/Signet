using System;
using System.Collections.Generic;
using System.IO;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// What a test made and has to let go of (books, temporary folders, view models), disposed together, the last added
/// first. A test class keeps one in a field and disposes it in its own <c>Dispose</c> (xUnit makes a new instance of the
/// class for each test and disposes it after the test), so that a helper can hand out what it made without a
/// <c>using</c> at every call.
/// </summary>
public sealed class Disposables : IDisposable
{
    private readonly List<IDisposable> _items = new();

    /// <summary>Takes <paramref name="item"/> to be disposed with the others and returns it.</summary>
    public T Add<T>(T item)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        return item;
    }

    /// <summary>Deletes the file at <paramref name="path"/> with the others (one the code under test made, e.g. a page it opened).</summary>
    public string DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _items.Add(new FileDeletion(path));
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            _items[i].Dispose();
        }

        _items.Clear();
    }

    private sealed class FileDeletion(string path) : IDisposable
    {
        public void Dispose()
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
