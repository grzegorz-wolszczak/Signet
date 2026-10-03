using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>
/// The view-independent model of the "Generate Table Of Contents" dialog (<c>HeadingSelector</c>): a tree of the
/// book's headings with the ability to enable/disable TOC entries, change the level and title,
/// and apply the changes to the XHTML resources.
/// </summary>
/// <remarks>
/// <para>
/// <b>All</b> changes are accumulated in the model and written only in <see cref="Apply"/> — cancelling
/// the dialog leaves no traces in the source (level and title changes, the <c>signet_not_in_toc</c> class
/// and auto-generated <c>id</c>s are all applied together).
/// </para>
/// <para>
/// Auto-generated <c>id</c>s use the prefix <see cref="SignetTocIdPrefix"/>.
/// A heading "at the start of the file" (<see cref="Heading.AtFileStart"/>) links to the file without a fragment and
/// loses its auto-generated <c>id</c>; a heading in the middle of the file gets an <c>id</c> if it has none.
/// </para>
/// </remarks>
public sealed class HeadingSelectorModel
{
    /// <summary>The prefix of auto-generated heading <c>id</c>s.</summary>
    public const string SignetTocIdPrefix = "signet_toc_id_";

    private static readonly string[] HeadingTags = { "h1", "h2", "h3", "h4", "h5", "h6" };

    private readonly Book _book;
    private IReadOnlyList<Heading> _headings;

    /// <summary>Loads all the book's headings (including excluded ones) and arranges them into a hierarchy.</summary>
    public HeadingSelectorModel(Book book)
    {
        _book = book ?? throw new ArgumentNullException(nameof(book));

        IReadOnlyList<Heading> flat =
            BookManipulation.Headings.GetHeadingList(book.GetHtmlResourcesExcludingNav(), includeUnwantedHeadings: true);
        MaxHeadingLevel = flat.Count > 0 ? flat.Max(h => h.Level) : 0;
        _headings = BookManipulation.Headings.MakeHeadingHierarchy(flat);
    }

    /// <summary>The top-level headings (the hierarchy). Modify them through the model's methods.</summary>
    public IReadOnlyList<Heading> RootHeadings => _headings;

    /// <summary>The highest (numerically) heading level occurring in the book (0 when there are no headings).</summary>
    public int MaxHeadingLevel { get; private set; }

    /// <summary>Sets whether the given heading goes into the TOC.</summary>
    public void SetInclusion(Heading heading, bool includeInToc)
    {
        ArgumentNullException.ThrowIfNull(heading);
        heading.IncludeInToc = includeInToc;
    }

    /// <summary>
    /// Bulk-selects headings for the TOC: <paramref name="upToLevel"/> = -1 all, 0 none,
    /// N only levels &lt;= N.
    /// </summary>
    public void SetAllHeadingInclusion(int upToLevel)
    {
        foreach (Heading heading in BookManipulation.Headings.GetFlattenedHeadings(_headings))
        {
            heading.IncludeInToc = upToLevel < 0 || heading.Level <= upToLevel;
        }
    }

    /// <summary>Changes the heading's title (the <c>title</c> attribute); deferred until <see cref="Apply"/>.</summary>
    public void SetTitle(Heading heading, string title)
    {
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(title);
        heading.Title = title;
        heading.Text = title.Length > 0 ? BookManipulation.Headings.Simplified(title) : heading.Text;
    }

    /// <summary>
    /// Changes the heading's level by <paramref name="delta"/> (clamped to 1–6) and rebuilds the
    /// hierarchy (rewriting the tag is deferred until <see cref="Apply"/>).
    /// </summary>
    /// <returns><c>true</c> when the level actually changed.</returns>
    public bool ChangeHeadingLevel(Heading heading, int delta)
    {
        ArgumentNullException.ThrowIfNull(heading);

        if (delta == 0 || heading.Level < 1 || heading.Level > 6)
        {
            return false;
        }

        int newLevel = heading.Level + delta;
        if (newLevel < 1 || newLevel > 6)
        {
            return false;
        }

        heading.Level = newLevel;

        List<Heading> flat = BookManipulation.Headings.GetFlattenedHeadings(_headings).ToList();
        foreach (Heading item in flat)
        {
            item.Children.Clear();
        }

        _headings = BookManipulation.Headings.MakeHeadingHierarchy(flat);
        MaxHeadingLevel = flat.Count > 0 ? flat.Max(h => h.Level) : 0;
        return true;
    }

    /// <summary>
    /// Writes all the changes (level, title, the <c>signet_not_in_toc</c> class, auto-generated <c>id</c>s)
    /// to the XHTML resources.
    /// </summary>
    /// <returns><c>true</c> when any resource was modified.</returns>
    public bool Apply()
    {
        List<string> usedIds = _book.GetIdsInHrefs().ToList();
        int nextTocId = 1;
        bool bookChanged = false;

        IReadOnlyList<Heading> itemOrder = BookManipulation.Headings.GetFlattenedHeadings(_headings);

        foreach (IGrouping<HtmlResource, Heading> group in itemOrder.GroupBy(h => h.ResourceFile))
        {
            HtmlResource resource = group.Key;
            string version = resource.EpubVersion;

            string source = CleanSource.StripXmlDeclaration(resource.GetText());
            IHtmlDocument document = XhtmlDoc.Parse(source);
            List<IElement> headingElements = document.QuerySelectorAll("h1, h2, h3, h4, h5, h6").ToList();

            bool fileChanged = false;

            foreach (Heading heading in group)
            {
                if (heading.HeadingIndex < 0 || heading.HeadingIndex >= headingElements.Count)
                {
                    continue;
                }

                IElement element = headingElements[heading.HeadingIndex];
                bool changed = false;

                // --- level (rewriting the tag) ---
                int currentLevel = element.LocalName.Length == 2 && element.LocalName[0] is 'h' or 'H'
                    ? element.LocalName[1] - '0'
                    : 0;
                if (heading.Level != currentLevel && currentLevel is >= 1 and <= 6)
                {
                    element = RenameElement(element, "h" + heading.Level);
                    headingElements[heading.HeadingIndex] = element;
                    changed = true;
                }

                // --- the signet_not_in_toc class ---
                string classAttribute = element.GetAttribute("class") ?? string.Empty;
                string newClass = classAttribute
                    .Replace(BookManipulation.Headings.SignetNotInTocClass, string.Empty, StringComparison.Ordinal)
                    .Trim();
                if (!heading.IncludeInToc)
                {
                    newClass = newClass.Length > 0 ? newClass + " " + BookManipulation.Headings.SignetNotInTocClass : BookManipulation.Headings.SignetNotInTocClass;
                }

                if (!string.Equals(newClass, classAttribute, StringComparison.Ordinal))
                {
                    if (newClass.Length > 0)
                    {
                        element.SetAttribute("class", newClass);
                    }
                    else
                    {
                        element.RemoveAttribute("class");
                    }

                    changed = true;
                }

                // --- auto-generated id ---
                string existingId = heading.Id;
                string newId = existingId;
                bool isGeneratedId = existingId.StartsWith(SignetTocIdPrefix, StringComparison.Ordinal);

                if (!heading.IncludeInToc || heading.AtFileStart)
                {
                    if (!usedIds.Contains(existingId) && isGeneratedId)
                    {
                        newId = string.Empty;
                    }
                }
                else
                {
                    if (!usedIds.Contains(existingId) && (existingId.Length == 0 || isGeneratedId))
                    {
                        do
                        {
                            newId = SignetTocIdPrefix + nextTocId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            nextTocId++;
                        }
                        while (usedIds.Contains(newId));
                    }
                }

                if (!string.Equals(newId.Trim(), existingId, StringComparison.Ordinal))
                {
                    heading.Id = newId;
                    if (newId.Length > 0)
                    {
                        element.SetAttribute("id", newId);
                    }
                    else
                    {
                        element.RemoveAttribute("id");
                    }

                    changed = true;
                }

                // --- title ---
                if (!string.Equals(heading.Title, heading.OrigTitle, StringComparison.Ordinal))
                {
                    if (heading.Title.Length > 0)
                    {
                        element.SetAttribute("title", heading.Title);
                    }
                    else
                    {
                        element.RemoveAttribute("title");
                    }

                    changed = true;
                }

                if (changed)
                {
                    heading.IsChanged = true;
                    fileChanged = true;
                }
            }

            if (fileChanged)
            {
                resource.SetText(CleanSource.ReserializeXhtmlDocument(document, version));
                bookChanged = true;
            }
        }

        return bookChanged;
    }

    /// <summary>The "Up to level N" entries for the drop-down list (without a description entry).</summary>
    public IReadOnlyList<HeadingLevelChoice> GetLevelChoices()
    {
        List<HeadingLevelChoice> choices = new();
        if (MaxHeadingLevel <= 0)
        {
            return choices;
        }

        choices.Add(new HeadingLevelChoice(CoreStrings.Get("Heading_LevelNone"), 0));
        for (int i = 1; i < MaxHeadingLevel; i++)
        {
            choices.Add(new HeadingLevelChoice(CoreStrings.Format("Heading_LevelUpTo", i), i));
        }

        choices.Add(new HeadingLevelChoice(CoreStrings.Get("Heading_LevelAll"), -1));
        return choices;
    }

    /// <summary>The number of enabled / hidden headings per level (<c>h1</c>–<c>h6</c>).</summary>
    public IReadOnlyDictionary<string, (int Included, int Hidden)> GetCounts()
    {
        Dictionary<string, (int Included, int Hidden)> counts = new(StringComparer.Ordinal);
        foreach (string tag in HeadingTags)
        {
            counts[tag] = (0, 0);
        }

        foreach (Heading heading in BookManipulation.Headings.GetFlattenedHeadings(_headings))
        {
            if (heading.Level is < 1 or > 6)
            {
                continue;
            }

            string tag = "h" + heading.Level;
            (int included, int hidden) = counts[tag];
            counts[tag] = heading.IncludeInToc ? (included + 1, hidden) : (included, hidden + 1);
        }

        return counts;
    }

    private static IElement RenameElement(IElement element, string newTagName)
    {
        IElement replacement = element.Owner!.CreateElement(newTagName);
        foreach (IAttr attribute in element.Attributes.ToList())
        {
            replacement.SetAttribute(attribute.Name, attribute.Value);
        }

        while (element.FirstChild is { } child)
        {
            replacement.AppendChild(child);
        }

        element.Replace(replacement);
        return replacement;
    }
}

/// <summary>An "Up to level N" list entry (<see cref="Level"/> = -1 all, 0 none, N the level).</summary>
public readonly record struct HeadingLevelChoice(string Label, int Level);
