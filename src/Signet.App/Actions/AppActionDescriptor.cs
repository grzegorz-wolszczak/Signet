namespace Signet.App.Actions;

/// <summary>
/// Immutable action description: identifier, source text (with an <c>&amp;</c> mnemonic),
/// default shortcut in portable key-sequence syntax (e.g. <c>Ctrl+Shift+S</c>, empty when none), icon key
/// and category (name of the main menu).
/// </summary>
/// <param name="Id">Identifier (see <see cref="AppActionIds"/>).</param>
/// <param name="Text">Source text, with an <c>&amp;</c> mnemonic.</param>
/// <param name="DefaultShortcut">Default shortcut in portable key-sequence syntax, or an empty string.</param>
/// <param name="IconKey">Icon file name without extension (from the main resources) or <see langword="null"/>.</param>
/// <param name="Category">Action category (main menu), e.g. <c>File</c>, <c>Format</c>.</param>
public sealed record AppActionDescriptor(
    string Id,
    string Text,
    string DefaultShortcut,
    string? IconKey,
    string Category);
