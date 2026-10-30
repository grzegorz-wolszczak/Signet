using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace Signet.App.Infrastructure;

/// <summary>
/// The title of a context menu: a bold, not clickable first item that says what the menu is about, with a line under it.
/// It is a plain <see cref="MenuItem"/> with the class <see cref="TitleClass"/> (a style for the items of a menu built from
/// view models can leave it out with <c>:not(.menuTitle)</c>).
/// </summary>
public static class MenuTitle
{
    /// <summary>The class of the title item.</summary>
    public const string TitleClass = "menuTitle";

    /// <summary>
    /// Puts a title with the text of <paramref name="text"/> at the top of <paramref name="menu"/>, followed by a separator.
    /// The text is read again each time the menu opens, so it follows the interface language.
    /// </summary>
    /// <returns>The title item.</returns>
    public static MenuItem AddTo(ContextMenu menu, Func<string> text)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(text);
        MenuItem title = new()
        {
            Header = text(),
            FontWeight = FontWeight.Bold,

            // Enabled, so that it is not drawn grayed out, but neither clickable nor reachable with the keyboard.
            IsHitTestVisible = false,
            Focusable = false,
        };
        title.Classes.Add(TitleClass);
        menu.Items.Insert(0, title);
        menu.Items.Insert(1, new Separator());
        menu.Opening += (_, _) => title.Header = text();
        return title;
    }
}
