using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Signet.Core.MiscEditors;

/// <summary>
/// The XML form of a library of entries in nested groups (the clips, the saved searches): every group is a
/// <c>&lt;group name="…"&gt;</c> element holding its entries and subgroups, so a name is a single attribute and may
/// contain any character (also <c>/</c>, which separates the groups only in a displayed full name).
/// </summary>
/// <remarks>
/// The flat list of entries (the leaves and the empty groups, in tree order, each with the names of its enclosing
/// groups) is what the editor models save and load; a group that holds anything is implied by its content.
/// </remarks>
internal static class GroupedXml
{
    /// <summary>Element name of a group.</summary>
    public const string GroupElement = "group";

    /// <summary>Attribute holding the name of a group or an entry.</summary>
    public const string NameAttribute = "name";

    /// <summary>
    /// Adds <paramref name="entries"/> to <paramref name="root"/>, nesting them in group elements; consecutive entries
    /// of the same group share one element.
    /// </summary>
    public static void Write<T>(
        XElement root,
        IEnumerable<T> entries,
        Func<T, IReadOnlyList<string>> groupPath,
        Func<T, bool> isGroup,
        Func<T, string> name,
        Func<T, XElement> leaf)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(entries);

        // The group elements open at the current position, outermost first.
        List<(string Name, XElement Element)> open = new();
        foreach (T entry in entries)
        {
            IReadOnlyList<string> path = groupPath(entry);
            int common = 0;
            while (common < open.Count && common < path.Count && string.Equals(open[common].Name, path[common], StringComparison.Ordinal))
            {
                common++;
            }

            open.RemoveRange(common, open.Count - common);
            foreach (string groupName in path.Skip(common))
            {
                open.Add((groupName, AddTo(open, root, Group(groupName))));
            }

            if (isGroup(entry))
            {
                // An empty group; a later entry inside it (should one come) goes into the same element.
                string groupName = name(entry);
                open.Add((groupName, AddTo(open, root, Group(groupName))));
            }
            else
            {
                AddTo(open, root, leaf(entry));
            }
        }
    }

    /// <summary>
    /// Reads the entries under <paramref name="root"/>: every <paramref name="leafElement"/> through
    /// <paramref name="leaf"/>, every group without content through <paramref name="emptyGroup"/>, both with the names
    /// of the enclosing groups. Other elements are ignored.
    /// </summary>
    public static IReadOnlyList<T> Read<T>(
        XElement root,
        string leafElement,
        Func<IReadOnlyList<string>, XElement, T> leaf,
        Func<IReadOnlyList<string>, string, T> emptyGroup)
    {
        ArgumentNullException.ThrowIfNull(root);
        List<T> result = new();
        Walk(root, Array.Empty<string>());
        return result;

        void Walk(XElement container, string[] path)
        {
            foreach (XElement child in container.Elements())
            {
                if (child.Name == GroupElement)
                {
                    string groupName = child.Attribute(NameAttribute)?.Value ?? string.Empty;
                    int before = result.Count;
                    Walk(child, path.Append(groupName).ToArray());
                    if (result.Count == before)
                    {
                        result.Add(emptyGroup(path, groupName));
                    }
                }
                else if (child.Name == leafElement)
                {
                    result.Add(leaf(path, child));
                }
            }
        }
    }

    /// <summary>The displayed full name: the group names and the name joined with <c>/</c>; a group ends with <c>/</c>.</summary>
    public static string FullName(IReadOnlyList<string> groupPath, string name, bool isGroup)
    {
        ArgumentNullException.ThrowIfNull(groupPath);
        string full = string.Join('/', groupPath.Append(name));
        return isGroup ? full + "/" : full;
    }

    private static XElement Group(string name) => new(GroupElement, new XAttribute(NameAttribute, name));

    private static XElement AddTo(List<(string Name, XElement Element)> open, XElement root, XElement element)
    {
        (open.Count > 0 ? open[^1].Element : root).Add(element);
        return element;
    }
}
