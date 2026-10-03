using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Signet.App.Input;
using Signet.App.Resources;
using Signet.App.Services;

namespace Signet.App.Actions;

/// <summary>
/// Builds and holds all <see cref="AppAction"/>s from <see cref="AppActionCatalog"/>,
/// registers them with <see cref="KeyboardShortcutManager"/> and wires up shortcut texts. Executing
/// an action without an attached handler shows a "not implemented" message in the status bar.
/// </summary>
public sealed class AppActionRegistry
{
    private readonly Dictionary<string, AppAction> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);
    private readonly KeyboardShortcutManager _shortcuts;
    private readonly IStatusBarService _statusBar;
    private readonly ILogger<AppActionRegistry> _logger;

    /// <summary>Creates the registry and initializes the actions from the catalog.</summary>
    public AppActionRegistry(
        KeyboardShortcutManager shortcuts,
        IStatusBarService statusBar,
        ILogger<AppActionRegistry> logger)
    {
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        foreach (AppActionDescriptor descriptor in AppActionCatalog.All)
        {
            AppAction action = new(descriptor);
            KeyboardShortcut shortcut = _shortcuts.RegisterAction(
                descriptor.Id, descriptor.DefaultShortcut, StripMnemonics(descriptor.Text));

            ApplyShortcut(action, shortcut);
            action.Invoked += OnActionInvoked;
            _actions[descriptor.Id] = action;
        }

        _shortcuts.ShortcutChanged += (_, id) =>
        {
            if (_actions.TryGetValue(id, out AppAction? action) && _shortcuts.Get(id) is { } sc)
            {
                ApplyShortcut(action, sc);
            }
        };
    }

    /// <summary>All actions.</summary>
    public IReadOnlyCollection<AppAction> Actions => _actions.Values;

    /// <summary>Returns the action for the id or <see langword="null"/>.</summary>
    public AppAction? Get(string id) => _actions.GetValueOrDefault(id);

    /// <summary>Returns the action for the id or throws when it is unknown.</summary>
    public AppAction Require(string id) =>
        _actions.TryGetValue(id, out AppAction? action)
            ? action
            : throw new KeyNotFoundException($"Unknown action: {id}");

    /// <summary>
    /// Attaches the action's implementation and enables it. Called by higher layers
    /// (e.g. TabManager, BookBrowser).
    /// </summary>
    public void SetHandler(string id, Action handler)
    {
        _handlers[id] = handler ?? throw new ArgumentNullException(nameof(handler));
        if (_actions.TryGetValue(id, out AppAction? action))
        {
            action.IsEnabled = true;
        }
    }

    /// <summary>Sets the enabled state of several actions at once.</summary>
    public void SetEnabled(bool enabled, params string[] ids)
    {
        foreach (string id in ids)
        {
            if (_actions.TryGetValue(id, out AppAction? action))
            {
                action.IsEnabled = enabled;
            }
        }
    }

    private void OnActionInvoked(object? sender, EventArgs e)
    {
        if (sender is not AppAction action)
        {
            return;
        }

        if (_handlers.TryGetValue(action.Id, out Action? handler))
        {
            handler();
            return;
        }

        string label = StripMnemonics(action.DefaultText);
        _logger.LogInformation("Action without implementation: {ActionId} ({Label})", action.Id, label);
        _statusBar.ShowMessage(Strings.Format("Status_NotImplemented", label), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
    }

    private static void ApplyShortcut(AppAction action, KeyboardShortcut shortcut)
    {
        action.Gesture = shortcut.KeyGesture;
        action.InputGestureText = KeyGestureConversion.ToPortableString(shortcut.KeyGesture);
    }

    private static string StripMnemonics(string text) =>
        text.Replace("&&", "\0").Replace("&", string.Empty).Replace("\0", "&");
}
