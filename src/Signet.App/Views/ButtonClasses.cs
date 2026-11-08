namespace Signet.App.Views;

/// <summary>The style classes of buttons (styles in <c>App.axaml</c>), for buttons made or changed in code.</summary>
public static class ButtonClasses
{
    /// <summary>Red: the button removes or deletes something.</summary>
    public const string Danger = "danger";

    /// <summary>
    /// Blue (Fluent's accent): the button saves or makes a change. The default button of a window (<c>IsDefault</c>, the one
    /// Enter presses) is blue without this class (a style in <c>App.axaml</c>) unless it closes (<c>IsCancel</c>) or is
    /// red / orange; the class is for a button that makes a change without being the default one.
    /// </summary>
    public const string Accent = "accent";

    /// <summary>Orange: the button discards unsaved changes.</summary>
    public const string Caution = "caution";
}
