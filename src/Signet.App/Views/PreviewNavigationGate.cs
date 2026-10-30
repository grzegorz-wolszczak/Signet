using System;

namespace Signet.App.Views;

/// <summary>
/// When the page of the preview may be loaded (<see cref="PreviewView"/>): only while the view is in the visual tree and
/// its web engine is initialized. A page asked for at another time waits, the latest one wins, and is loaded as soon as
/// both hold again.
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

    /// <summary>The page waiting to be loaded, or <c>null</c>.</summary>
    public Uri? Pending => _pending;

    /// <summary>A page is asked for: returns it when it can be loaded now, else keeps it for later and returns <c>null</c>.</summary>
    public Uri? Request(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (_attached && _initialized)
        {
            _pending = null;
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
        return url;
    }
}
