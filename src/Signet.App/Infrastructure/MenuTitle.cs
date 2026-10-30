using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Signet.App.Resources;

namespace Signet.App.Infrastructure;

/// <summary>
/// The title of a context menu: a bold, not clickable first item naming what was right-clicked (a file, an entry, a
/// toolbar; with several selected, how many), or the place when nothing in particular was, with a line under it.
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
        MenuItem title = CreateItem(text());
        menu.Items.Insert(0, title);
        menu.Items.Insert(1, new Separator());
        // Opening comes before a right click opens the menu; a menu opened from code (ContextMenu.Open) skips it, so the
        // text is also read once it is open.
        menu.Opening += (_, _) => title.Header = Escape(text());
        menu.Opened += (_, _) => title.Header = Escape(text());
        return title;
    }

    /// <summary>
    /// A text as a menu header shows it literally: a menu item takes "_" for the mark of its access key, so a file name
    /// such as "ch_01.xhtml" would lose it. The underscores are doubled.
    /// </summary>
    public static string Escape(string text) => (text ?? string.Empty).Replace("_", "__", StringComparison.Ordinal);

    /// <summary><see cref="Escape"/> for a title bound in XAML (<c>Header="{Binding Name, Converter=…}"</c>).</summary>
    public static IValueConverter EscapeConverter { get; } = new FuncValueConverter<string?, string>(t => Escape(t ?? string.Empty));

    /// <summary>
    /// The title item alone, for a menu that builds its items anew on every opening (it puts the title and a separator
    /// first itself).
    /// </summary>
    public static MenuItem CreateItem(string text)
    {
        MenuItem title = new()
        {
            Header = Escape(text),
            FontWeight = FontWeight.Bold,

            // Enabled, so that it is not drawn grayed out, but neither clickable nor reachable with the keyboard.
            IsHitTestVisible = false,
            Focusable = false,
        };
        title.Classes.Add(TitleClass);
        return title;
    }

    /// <summary>
    /// The title for a menu on a selection: the name of the one selected element, "N selected" for several, or
    /// <paramref name="place"/> when nothing is selected.
    /// </summary>
    public static string ForSelection(IEnumerable<string> names, string place)
    {
        ArgumentNullException.ThrowIfNull(names);
        List<string> list = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        return list.Count switch
        {
            0 => place,
            1 => list[0],
            _ => Strings.Format("MenuTitle_Selected", list.Count),
        };
    }
}
