using System.Globalization;
using System.Text.RegularExpressions;
using Signet.App.Resources;

namespace Signet.App.Actions;

/// <summary>
/// Translations of menu texts: actions from <see cref="AppActionCatalog"/>, their categories and
/// submenu headers from <see cref="Menu.MenuLayout"/>. The catalog and the menu layout hold English
/// source texts (used for identification); here they are replaced with text from the resources:
/// <c>Action_&lt;id with "." → "_"&gt;</c>, <c>ActionCategory_&lt;category without spaces&gt;</c>,
/// <c>Menu_&lt;header without characters outside A–Z, 0–9&gt;</c>. Missing key = source text.
/// </summary>
public static partial class ActionTexts
{
    /// <summary>Action text (with an <c>&amp;</c> mnemonic) in the UI language.</summary>
    public static string Text(AppActionDescriptor descriptor)
    {
        System.ArgumentNullException.ThrowIfNull(descriptor);
        if (AppActionIds.TryGetClipSlot(descriptor.Id, out int slot))
        {
            return Strings.Format("Action_ClipSlot", slot.ToString(CultureInfo.CurrentCulture));
        }

        return Strings.TryGet("Action_" + descriptor.Id.Replace('.', '_')) ?? descriptor.Text;
    }

    /// <summary>Action category name in the UI language.</summary>
    public static string Category(string category)
    {
        System.ArgumentNullException.ThrowIfNull(category);
        return Strings.TryGet("ActionCategory_" + category.Replace(" ", string.Empty, System.StringComparison.Ordinal)) ?? category;
    }

    /// <summary>Submenu header (with an <c>&amp;</c> mnemonic) in the UI language.</summary>
    public static string MenuHeader(string header)
    {
        System.ArgumentNullException.ThrowIfNull(header);
        return Strings.TryGet("Menu_" + NonAlphanumeric().Replace(header, string.Empty)) ?? header;
    }

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
