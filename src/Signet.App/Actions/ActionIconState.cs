namespace Signet.App.Actions;

/// <summary>
/// Visual state of an action's icon on the toolbar and in the menu, independent of whether the action
/// is enabled (e.g. "Save": gray when there are no changes, red when there are — the action works in both states).
/// </summary>
public enum ActionIconState
{
    /// <summary>The icon's own colors.</summary>
    Normal,

    /// <summary>Grayed-out icon (nothing to do), even though the action is enabled.</summary>
    Inactive,

    /// <summary>Red-tinted icon — needs attention (e.g. unsaved changes).</summary>
    Attention,
}
