using System;

namespace Signet.Core.Misc;

/// <summary>Arguments of the <see cref="SettingsStore.SettingChanged"/> event.</summary>
/// <param name="qualifiedKey">Full key of the changed setting in the form <c>group/key</c>.</param>
public sealed class SettingChangedEventArgs(string qualifiedKey) : EventArgs
{
    /// <summary>Full key of the changed setting (<c>group/key</c>).</summary>
    public string QualifiedKey { get; } = qualifiedKey;
}
