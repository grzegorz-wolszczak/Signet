using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Signet.Core.Misc;

namespace Signet.App.Services;

/// <summary>
/// Clipboard history (tracking only, no dialog): polls the system clipboard in the background and
/// persists successive unique text values in <see cref="SettingsStore.ClipboardHistory"/>
/// (newest first, limited by <see cref="SettingsStore.ClipboardHistoryLimit"/>).
/// </summary>
/// <remarks>
/// Avalonia has no "clipboard changed" event, so instead of a subscription the service uses
/// periodic polling of <see cref="ClipboardExtensions.TryGetTextAsync(IClipboard)"/>
/// (<see cref="PollInterval"/>) —
/// active for the whole lifetime of the service (without distinguishing window
/// activation/deactivation, which would not change the behavior in a single-window desktop
/// application).
/// </remarks>
public sealed class ClipboardHistoryService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _timer;
    private readonly List<string> _items;
    private IClipboard? _clipboard;
    private string? _lastSeenText;

    /// <summary>Creates the service and loads the existing history from <paramref name="settings"/>.</summary>
    public ClipboardHistoryService(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _items = settings.ClipboardHistory.ToList();
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => _ = PollAsync();
    }

    /// <summary>Raised after every history change (a new entry or loading from the settings).</summary>
    public event EventHandler? Changed;

    /// <summary>History entries, newest first.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Attaches the system clipboard and starts periodic polling.</summary>
    public void AttachClipboard(IClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        _clipboard = clipboard;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    /// <summary>Stops polling the clipboard.</summary>
    public void Stop() => _timer.Stop();

    private async Task PollAsync()
    {
        if (_clipboard is null)
        {
            return;
        }

        string? text;
        try
        {
            text = await _clipboard.TryGetTextAsync();
        }
        catch (Exception)
        {
            return;
        }

        ApplyClipboardText(text);
    }

    /// <summary>
    /// History update logic for a value read from the clipboard — extracted from
    /// <see cref="PollAsync"/> so that it can be tested without faking the whole
    /// <see cref="IClipboard"/> API. Public so that tests can simulate a clipboard
    /// read without a real platform implementation.
    /// </summary>
    public void ApplyClipboardText(string? text)
    {
        if (string.IsNullOrEmpty(text) || string.Equals(text, _lastSeenText, StringComparison.Ordinal))
        {
            return;
        }

        _lastSeenText = text;

        int limit = _settings.ClipboardHistoryLimit;
        if (limit <= 0)
        {
            return;
        }

        int existingIndex = _items.FindIndex(s => string.Equals(s, text, StringComparison.Ordinal));
        if (existingIndex == 0)
        {
            return;
        }

        if (existingIndex > 0)
        {
            _items.RemoveAt(existingIndex);
        }

        _items.Insert(0, text);
        while (_items.Count > limit)
        {
            _items.RemoveAt(_items.Count - 1);
        }

        _settings.ClipboardHistory = _items;
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose() => _timer.Stop();
}
