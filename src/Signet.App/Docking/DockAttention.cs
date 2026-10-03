using Avalonia;

namespace Signet.App.Docking;

/// <summary>
/// Attached property that marks a dock tab (a tool tab strip item or a collapsed "Auto Hide" tab)
/// with the <c>attention</c> class. A style in <c>App.axaml</c> binds it to
/// <see cref="SignetTool.NeedsAttention"/> and draws such a tab with the warning color — Dock's tab
/// templates cannot be restyled from the dockable model directly.
/// </summary>
public static class DockAttention
{
    /// <summary>The CSS-like class set on the tab while attention is needed.</summary>
    public const string ClassName = "attention";

    /// <summary>Whether the tab needs attention.</summary>
    public static readonly AttachedProperty<bool> IsActiveProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, bool>("IsActive", typeof(DockAttention));

    static DockAttention()
    {
        IsActiveProperty.Changed.AddClassHandler<StyledElement>(
            (element, e) => element.Classes.Set(ClassName, e.NewValue is true));
    }

    /// <summary>Gets <see cref="IsActiveProperty"/>.</summary>
    public static bool GetIsActive(StyledElement element) => element.GetValue(IsActiveProperty);

    /// <summary>Sets <see cref="IsActiveProperty"/>.</summary>
    public static void SetIsActive(StyledElement element, bool value) => element.SetValue(IsActiveProperty, value);
}
