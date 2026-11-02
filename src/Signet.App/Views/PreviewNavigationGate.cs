using System;

namespace Signet.App.Views;

/// <summary>
/// When the page of the preview may be loaded (<see cref="PreviewView"/>): only while the view is in the visual tree and
/// its web engine is initialized. A page asked for at another time waits, the latest one wins, and is loaded as soon as
/// both hold again.
/// <para>It also knows the page given to the engine whose load has not completed yet (<see cref="ForReload"/>).</para>
/// </summary>
/// <remarks>
/// A closed Preview panel keeps its view out of the visual tree while the preview follows the active tab. A page loaded
/// then was lost (the native web view does not navigate while it is detached): opened again, the panel showed the page
/// of the tab that was active when it was closed, and a click in it went to the wrong file.
/// </remarks>
public sealed class PreviewNavigationGate
{
    private bool _attached;
    private bool _initialized;
    private Uri? _pending;
    private Uri? _loading;
    private Uri? _shown;

    /// <summary>The page waiting to be loaded, or <c>null</c>.</summary>
    public Uri? Pending => _pending;

    /// <summary>A page is asked for: returns it when it can be loaded now, else keeps it for later and returns <c>null</c>.</summary>
    public Uri? Request(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (_attached && _initialized)
        {
            _pending = null;
            Loading(url);
            return url;
        }

        _pending = url;
        return null;
    }

    /// <summary>Forgets the page waiting (nothing is to be shown).</summary>
    public void Forget() => _pending = null;

    /// <summary>The view entered the visual tree: returns the page to load now, if one waits.</summary>
    public Uri? Attached()
    {
        _attached = true;
        return TakeIfReady();
    }

    /// <summary>The view left the visual tree: pages asked for from now on wait.</summary>
    public void Detached() => _attached = false;

    /// <summary>The web engine is initialized: returns the page to load now, if one waits.</summary>
    public Uri? Initialized()
    {
        _initialized = true;
        return TakeIfReady();
    }

    private Uri? TakeIfReady()
    {
        if (!_attached || !_initialized || _pending is null)
        {
            return null;
        }

        Uri url = _pending;
        _pending = null;
        Loading(url);
        return url;
    }

    // A page given to the engine. Another place in the page shown (only the fragment differs) is not a load: the engine
    // scrolls without reporting a completion.
    private void Loading(Uri url) => _loading = _shown is not null && SamePage(url, _shown) ? null : url;

    /// <summary>
    /// A load has completed (successfully or not): the page asked for is shown when <paramref name="url"/> is that page
    /// (the fragment aside) or unknown. The completion of an older load, still reported, changes nothing.
    /// </summary>
    public void Completed(Uri? url)
    {
        if (_loading is null || url is null || SamePage(_loading, url))
        {
            _shown = url ?? _loading;
            _loading = null;
        }
    }

    /// <summary>
    /// A reload is asked for: the page whose load has not completed yet, to be loaded again instead, or <c>null</c> when
    /// the engine's own reload is right. The engine reloads the page it has; while a load it was given is under way that
    /// is still the previous page — after the shown file was merged into another, a file gone from the mirror (found in
    /// November 2026: on Linux the navigation waits for the UI thread's decision, and the reload that follows at once
    /// came first).
    /// </summary>
    public Uri? ForReload() => _loading;

    private static bool SamePage(Uri a, Uri b) =>
        Uri.Compare(a, b, UriComponents.AbsoluteUri & ~UriComponents.Fragment, UriFormat.Unescaped, StringComparison.Ordinal) == 0;
}
