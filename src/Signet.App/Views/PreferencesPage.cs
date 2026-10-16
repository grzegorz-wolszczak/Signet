using Avalonia;
using Avalonia.Controls;

namespace Signet.App.Views;

/// <summary>
/// Where a page of the Preferences window (a tab of its <see cref="TabControl"/> with the hidden tab strip) sits in the
/// section tree: <see cref="ParentProperty"/> names the page of the enclosing section. A page without it is a top-level
/// section. Subsections follow their parent in the XAML, in the order they are listed in the tree.
/// </summary>
public static class PreferencesPage
{
    /// <summary>The <c>x:Name</c> of the page of the enclosing section; <c>null</c> for a top-level section.</summary>
    public static readonly AttachedProperty<string?> ParentProperty =
        AvaloniaProperty.RegisterAttached<TabItem, string?>("Parent", typeof(PreferencesPage));

    /// <summary>Gets <see cref="ParentProperty"/>.</summary>
    public static string? GetParent(TabItem page) => page.GetValue(ParentProperty);

    /// <summary>Sets <see cref="ParentProperty"/>.</summary>
    public static void SetParent(TabItem page, string? value) => page.SetValue(ParentProperty, value);

    /// <summary>
    /// On a link of a page: the <c>x:Name</c> of the page it opens (e.g. "Appearance" on the Preview section opens
    /// Appearance › Preview).
    /// </summary>
    public static readonly AttachedProperty<string?> TargetProperty =
        AvaloniaProperty.RegisterAttached<HyperlinkButton, string?>("Target", typeof(PreferencesPage));

    /// <summary>
    /// On a link of a page that opens a page of the color scheme: the id of the key (e.g. <c>ui.bookBrowser.background</c>)
    /// to select there, with its group expanded.
    /// </summary>
    public static readonly AttachedProperty<string?> TargetKeyProperty =
        AvaloniaProperty.RegisterAttached<HyperlinkButton, string?>("TargetKey", typeof(PreferencesPage));

    /// <summary>Gets <see cref="TargetKeyProperty"/>.</summary>
    public static string? GetTargetKey(HyperlinkButton link) => link.GetValue(TargetKeyProperty);

    /// <summary>Sets <see cref="TargetKeyProperty"/>.</summary>
    public static void SetTargetKey(HyperlinkButton link, string? value) => link.SetValue(TargetKeyProperty, value);

    /// <summary>Gets <see cref="TargetProperty"/>.</summary>
    public static string? GetTarget(HyperlinkButton link) => link.GetValue(TargetProperty);

    /// <summary>Sets <see cref="TargetProperty"/>.</summary>
    public static void SetTarget(HyperlinkButton link, string? value) => link.SetValue(TargetProperty, value);
}
