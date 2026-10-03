using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Resources;

namespace Signet.Core.MainUI;

/// <summary>
/// The view-independent model for managing open tabs.
/// </summary>
/// <remarks>
/// The model holds an ordered list of <see cref="OpenTab"/> (a resource + <see cref="ContentTabKind"/>),
/// and knows the active tab and the activation history (for <see cref="Back"/>). It knows neither Avalonia nor
/// Dock.Avalonia — mapping to <c>DocumentDock</c> and creating the concrete tab views
/// belong to the App layer (<c>Signet.App.Tabs.TabManager</c>).
/// <para>
/// A duplicate tab for the same resource is prevented by
/// <see cref="OpenResource"/>; <see cref="CloseTab(int,bool)"/>
/// ignores an attempt to close the last tab without <c>force</c>;
/// removing a resource from the book automatically closes its tab; renaming/moving a resource raises
/// <see cref="TabCaptionChanged"/>.
/// </para>
/// </remarks>
public sealed class TabManagerModel : IDisposable
{
    private const int MaxHistory = 50;

    private readonly List<OpenTab> _tabs = new();
    private readonly List<OpenTab> _history = new();
    private int _activeIndex = -1;
    private bool _disposed;

    /// <summary>Raised after a new tab is added. Carries the tab and its position.</summary>
    public event EventHandler<TabEventArgs>? TabOpened;

    /// <summary>Raised after a tab is removed. Carries the removed tab and its former position.</summary>
    public event EventHandler<TabEventArgs>? TabClosed;

    /// <summary>Raised when the active tab changes. Carries the new active tab.</summary>
    public event EventHandler<TabEventArgs>? TabActivated;

    /// <summary>Raised when a tab's caption changes (resource rename/move).</summary>
    public event EventHandler<TabEventArgs>? TabCaptionChanged;

    /// <summary>Raised when the last tab was closed (the document area is empty).</summary>
    public event EventHandler? TabsEmptied;

    /// <summary>The open tabs in display order.</summary>
    public IReadOnlyList<OpenTab> Tabs => _tabs;

    /// <summary>The number of open tabs.</summary>
    public int Count => _tabs.Count;

    /// <summary>The position of the active tab or <c>-1</c> when nothing is open.</summary>
    public int ActiveIndex => _activeIndex;

    /// <summary>The active tab or <c>null</c>.</summary>
    public OpenTab? ActiveTab =>
        _activeIndex >= 0 && _activeIndex < _tabs.Count ? _tabs[_activeIndex] : null;

    /// <summary>Whether <see cref="Back"/> has somewhere to go back to.</summary>
    public bool CanGoBack => _history.Count > 0;

    /// <summary>
    /// Opens a resource in a new tab of the proper kind. If the resource is already open —
    /// activates the existing tab.
    /// </summary>
    /// <param name="resource">The resource to open.</param>
    /// <param name="precedeCurrent">
    /// When <c>true</c>, the new tab is inserted right before the current one and does <em>not</em> become active.
    /// </param>
    public OpenTab OpenResource(Resource resource, bool precedeCurrent = false)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ThrowIfDisposed();

        int existing = IndexOfResource(resource);
        if (existing >= 0)
        {
            Activate(existing);
            return _tabs[existing];
        }

        OpenTab tab = new(resource, ContentTabKindMap.ForResource(resource));
        Attach(tab);

        if (precedeCurrent && _activeIndex >= 0)
        {
            int insertAt = _activeIndex;
            _tabs.Insert(insertAt, tab);
            _activeIndex++;
            TabOpened?.Invoke(this, new TabEventArgs(tab, insertAt));
        }
        else
        {
            int insertAt = _tabs.Count;
            _tabs.Add(tab);
            TabOpened?.Invoke(this, new TabEventArgs(tab, insertAt));
            Activate(insertAt);
        }

        return tab;
    }

    /// <summary>Activates the tab with the given resource, if it is open.</summary>
    public void ActivateResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        int index = IndexOfResource(resource);
        if (index >= 0)
        {
            Activate(index);
        }
    }

    /// <summary>Activates the tab at the given position (storing the previous one in the history).</summary>
    public void Activate(int index) => SetActive(index, recordHistory: true);

    /// <summary>
    /// Closes the tab at the given position. Without <paramref name="force"/> it ignores an attempt to close
    /// the last open tab.
    /// </summary>
    /// <returns><c>true</c> when the tab was closed.</returns>
    public bool CloseTab(int index, bool force = false)
    {
        ThrowIfDisposed();
        if (index < 0 || index >= _tabs.Count)
        {
            return false;
        }

        if (!force && _tabs.Count <= 1)
        {
            return false;
        }

        OpenTab tab = _tabs[index];
        Detach(tab);
        _history.Remove(tab);
        _tabs.RemoveAt(index);

        bool wasActive = index == _activeIndex;
        if (index < _activeIndex)
        {
            _activeIndex--;
        }

        if (_tabs.Count == 0)
        {
            _activeIndex = -1;
            TabClosed?.Invoke(this, new TabEventArgs(tab, index));
            TabsEmptied?.Invoke(this, EventArgs.Empty);
            return true;
        }

        int nextActive = _activeIndex;
        if (wasActive)
        {
            nextActive = Math.Min(index, _tabs.Count - 1);
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                int hi = _tabs.IndexOf(_history[i]);
                if (hi >= 0)
                {
                    nextActive = hi;
                    break;
                }
            }

            _activeIndex = -1;
        }

        TabClosed?.Invoke(this, new TabEventArgs(tab, index));

        if (wasActive)
        {
            SetActive(nextActive, recordHistory: false);
        }

        return true;
    }

    /// <summary>Closes the active tab.</summary>
    public bool CloseActiveTab() => _activeIndex >= 0 && CloseTab(_activeIndex);

    /// <summary>Closes the tab with the given resource, if it is open.</summary>
    public void CloseResource(Resource resource, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(resource);
        int index = IndexOfResource(resource);
        if (index >= 0)
        {
            CloseTab(index, force);
        }
    }

    /// <summary>Closes all tabs except the given one.</summary>
    public void CloseOtherTabs(int keepIndex)
    {
        ThrowIfDisposed();
        if (_tabs.Count <= 1 || keepIndex < 0 || keepIndex >= _tabs.Count)
        {
            return;
        }

        for (int i = _tabs.Count - 1; i > keepIndex; i--)
        {
            CloseTab(i, force: true);
        }

        for (int i = keepIndex - 1; i >= 0; i--)
        {
            CloseTab(0, force: true);
        }
    }

    /// <summary>Closes all tabs except the active one.</summary>
    public void CloseOtherTabs()
    {
        if (_activeIndex >= 0)
        {
            CloseOtherTabs(_activeIndex);
        }
    }

    /// <summary>Closes all tabs.</summary>
    public void CloseAllTabs()
    {
        ThrowIfDisposed();
        while (_tabs.Count > 0)
        {
            CloseTab(0, force: true);
        }
    }

    /// <summary>Activates the next tab (wraps around).</summary>
    public void NextTab()
    {
        if (_tabs.Count == 0 || _activeIndex < 0)
        {
            return;
        }

        int next = _activeIndex == _tabs.Count - 1 ? 0 : _activeIndex + 1;
        if (next != _activeIndex)
        {
            Activate(next);
        }
    }

    /// <summary>Activates the previous tab (wraps around).</summary>
    public void PreviousTab()
    {
        if (_tabs.Count == 0 || _activeIndex < 0)
        {
            return;
        }

        int previous = _activeIndex == 0 ? _tabs.Count - 1 : _activeIndex - 1;
        if (previous != _activeIndex)
        {
            Activate(previous);
        }
    }

    /// <summary>
    /// Goes back to the most recently active tab from the history (without pushing the current one onto the stack —
    /// subsequent calls step further back).
    /// </summary>
    public void Back()
    {
        while (_history.Count > 0)
        {
            OpenTab candidate = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            int index = _tabs.IndexOf(candidate);
            if (index >= 0 && index != _activeIndex)
            {
                SetActive(index, recordHistory: false);
                return;
            }
        }
    }

    /// <summary>A session snapshot: the bookpaths of the open tabs + the active one's bookpath.</summary>
    public TabSession CaptureSession() =>
        new(_tabs.Select(t => t.Resource.BookPath).ToArray(), ActiveTab?.Resource.BookPath);

    /// <summary>
    /// Restores a session: opens the resources named by <paramref name="session"/> (resolved through
    /// <paramref name="resolveBookPath"/>) and activates the saved tab. Bookpaths that are not found
    /// are skipped.
    /// </summary>
    public void RestoreSession(TabSession session, Func<string, Resource?> resolveBookPath)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolveBookPath);
        ThrowIfDisposed();

        foreach (string bookPath in session.OpenBookPaths)
        {
            if (resolveBookPath(bookPath) is { } resource)
            {
                OpenResource(resource);
            }
        }

        if (session.ActiveBookPath is { } activePath && resolveBookPath(activePath) is { } active)
        {
            ActivateResource(active);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (OpenTab tab in _tabs)
        {
            Detach(tab);
        }

        _tabs.Clear();
        _history.Clear();
        _activeIndex = -1;
        _disposed = true;
    }

    private void SetActive(int index, bool recordHistory)
    {
        ThrowIfDisposed();
        if (index < 0 || index >= _tabs.Count || index == _activeIndex)
        {
            return;
        }

        if (recordHistory && ActiveTab is { } previous)
        {
            _history.Remove(previous);
            _history.Add(previous);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveAt(0);
            }
        }

        _activeIndex = index;
        TabActivated?.Invoke(this, new TabEventArgs(_tabs[index], index));
    }

    private int IndexOfResource(Resource resource)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (string.Equals(_tabs[i].Resource.Identifier, resource.Identifier, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void Attach(OpenTab tab)
    {
        tab.Resource.Deleted += OnResourceDeleted;
        tab.Resource.Renamed += OnResourcePathChanged;
        tab.Resource.Moved += OnResourcePathChanged;
    }

    private void Detach(OpenTab tab)
    {
        tab.Resource.Deleted -= OnResourceDeleted;
        tab.Resource.Renamed -= OnResourcePathChanged;
        tab.Resource.Moved -= OnResourcePathChanged;
    }

    private void OnResourceDeleted(object? sender, EventArgs e)
    {
        if (sender is Resource resource)
        {
            CloseResource(resource, force: true);
        }
    }

    private void OnResourcePathChanged(object? sender, ResourcePathChangedEventArgs e)
    {
        if (sender is not Resource resource)
        {
            return;
        }

        int index = IndexOfResource(resource);
        if (index >= 0)
        {
            TabCaptionChanged?.Invoke(this, new TabEventArgs(_tabs[index], index));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

/// <summary>
/// A single open tab: a resource plus the kind of view it opens in.
/// </summary>
public sealed class OpenTab
{
    internal OpenTab(Resource resource, ContentTabKind kind)
    {
        Resource = resource;
        Kind = kind;
    }

    /// <summary>The resource displayed in the tab.</summary>
    public Resource Resource { get; }

    /// <summary>The tab's view kind.</summary>
    public ContentTabKind Kind { get; }

    /// <summary>The resource identifier (stable for the tab's whole lifetime).</summary>
    public string Identifier => Resource.Identifier;

    /// <summary>
    /// The tab caption: a unique shortened path fragment, or the file name when none is set.
    /// </summary>
    public string Caption =>
        Resource.ShortPathName is { Length: > 0 } shortName ? shortName : Resource.Filename;
}

/// <summary>The arguments of <see cref="TabManagerModel"/> events concerning a single tab.</summary>
public sealed class TabEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    public TabEventArgs(OpenTab tab, int index)
    {
        Tab = tab;
        Index = index;
    }

    /// <summary>The tab the event concerns.</summary>
    public OpenTab Tab { get; }

    /// <summary>The tab's position (for <see cref="TabManagerModel.TabClosed"/> — the position before removal).</summary>
    public int Index { get; }
}

/// <summary>The remembered tab session state — persisted in settings and restored after a restart.</summary>
/// <param name="OpenBookPaths">The bookpaths of the open tabs in display order.</param>
/// <param name="ActiveBookPath">The active tab's bookpath or <c>null</c>.</param>
public sealed record TabSession(IReadOnlyList<string> OpenBookPaths, string? ActiveBookPath);
