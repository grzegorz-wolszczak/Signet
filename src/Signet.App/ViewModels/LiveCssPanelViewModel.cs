using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Parsers;

namespace Signet.App.ViewModels;

/// <summary>
/// A single CSS declaration in the Live CSS panel — <see cref="Text"/> is ready for display,
/// <see cref="IsOverridden"/> drives the strikethrough and <see cref="IsDimmed"/> the dimming in the view;
/// <see cref="OverriddenByText"/> says which rule wins instead.
/// </summary>
public sealed class LiveCssDeclarationViewModel
{
    internal LiveCssDeclarationViewModel(CssMatchedDeclaration declaration, Func<string, string> resolveBookText)
    {
        Text = declaration.IsImportant
            ? $"{declaration.Property}: {declaration.Value} !important;"
            : $"{declaration.Property}: {declaration.Value};";
        IsOverridden = declaration.IsOverridden;
        IsDimmed = !declaration.AffectsElement;
        OverriddenByText = declaration.OverriddenBy is { } source
            ? Strings.Format("LiveCssPanelWindow_OverriddenBy", LiveCssFormat.Source(source, resolveBookText))
            : string.Empty;
    }

    /// <summary>Declaration text to display (<c>property: value;</c>).</summary>
    public string Text { get; }

    /// <summary>
    /// Whether this declaration is overridden — by a rule with a higher cascade priority on the same element,
    /// or (inherited properties) by a declaration on an element closer to the inspected one.
    /// </summary>
    public bool IsOverridden { get; }

    /// <summary>
    /// Whether the declaration does not take effect on the inspected element (overridden, or a non-inherited
    /// property of an ancestor) — shown dimmed.
    /// </summary>
    public bool IsDimmed { get; }

    /// <summary>"(overridden by selector, file:line)" for an overridden declaration, otherwise empty.</summary>
    public string OverriddenByText { get; }

    /// <summary>Whether <see cref="OverriddenByText"/> is shown.</summary>
    public bool HasOverriddenBy => OverriddenByText.Length > 0;
}

/// <summary>A single CSS rule matched to the element, with a clickable jump to its declaration.</summary>
public sealed class LiveCssRuleViewModel
{
    internal LiveCssRuleViewModel(CssMatchedRule rule, Func<string, string> resolveBookText, Action<string, int> jumpTo)
    {
        Header = rule.IsInlineStyle
            ? Strings.Get("LiveCssPanelWindow_Inline")
            : FormatHeader(rule, resolveBookText);
        Declarations = new ObservableCollection<LiveCssDeclarationViewModel>(
            rule.Declarations.Select(d => new LiveCssDeclarationViewModel(d, resolveBookText)));
        JumpCommand = new RelayCommand(() => jumpTo(rule.BookPath, rule.Offset));
    }

    /// <summary>Rule header: selector, file:line, specificity.</summary>
    public string Header { get; }

    /// <summary>The rule's declarations, with the cascade result per property.</summary>
    public ObservableCollection<LiveCssDeclarationViewModel> Declarations { get; }

    /// <summary>Opens (or activates) the tab with this rule and places the caret on its selector.</summary>
    public RelayCommand JumpCommand { get; }

    private static string FormatHeader(CssMatchedRule rule, Func<string, string> resolveBookText)
    {
        string specificity = string.Create(
            CultureInfo.InvariantCulture,
            $"{rule.Specificity.Ids},{rule.Specificity.Classes},{rule.Specificity.Types}");
        return $"{rule.SelectorText}  —  {LiveCssFormat.Location(rule.BookPath, rule.Offset, resolveBookText)}  —  "
            + $"{Strings.Get("LiveCssPanelWindow_Specificity")} {specificity}";
    }
}

/// <summary>
/// One level of the element hierarchy in the Live CSS panel (from <c>body</c> down to the element under
/// the caret) with the rules matched to that element.
/// </summary>
public sealed class LiveCssLevelViewModel
{
    internal LiveCssLevelViewModel(CssCascadeLevel level, Func<string, string> resolveBookText, Action<string, int> jumpTo)
    {
        Header = level.IsInspectedElement
            ? $"{level.ElementDescription}  {Strings.Get("LiveCssPanelWindow_InspectedElement")}"
            : level.ElementDescription;
        IsInspectedElement = level.IsInspectedElement;
        Rules = new ObservableCollection<LiveCssRuleViewModel>(
            level.MatchedRules.Select(r => new LiveCssRuleViewModel(r, resolveBookText, jumpTo)));
    }

    /// <summary>Element description (e.g. <c>div.a</c>), marked for the element under the caret.</summary>
    public string Header { get; }

    /// <summary>Whether this is the element under the caret (the last level).</summary>
    public bool IsInspectedElement { get; }

    /// <summary>Rules matched to this element, in source cascade order (inline style last).</summary>
    public ObservableCollection<LiveCssRuleViewModel> Rules { get; }

    /// <summary>Whether to show the "no rules" message (only the inspected element can have none).</summary>
    public bool ShowNoRules => Rules.Count == 0;
}

/// <summary>Formatting shared by the Live CSS panel view models.</summary>
internal static class LiveCssFormat
{
    /// <summary>"selector, file:line" (or the inline-style label with the HTML file:line).</summary>
    public static string Source(CssDeclarationSource source, Func<string, string> resolveBookText)
    {
        return $"{source.SelectorText}, {Location(source.BookPath, source.Offset, resolveBookText)}";
    }

    /// <summary>"file:line" — the file name without folders, the 1-based line of <paramref name="offset"/>.</summary>
    public static string Location(string bookPath, int offset, Func<string, string> resolveBookText)
    {
        string shortPath = bookPath.Contains('/', StringComparison.Ordinal)
            ? bookPath[(bookPath.LastIndexOf('/') + 1)..]
            : bookPath;
        return string.Create(CultureInfo.InvariantCulture, $"{shortPath}:{LineOf(resolveBookText(bookPath), offset)}");
    }

    private static int LineOf(string text, int offset)
    {
        int line = 1;
        int limit = Math.Min(offset, text.Length);
        for (int i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}

/// <summary>
/// View model of the "Live CSS Panel" window — for the most recently chosen element
/// (<see cref="MainWindowViewModel.TryResolveLiveCssPanel"/> for the caret of the active Code View tab)
/// shows the element hierarchy from <c>body</c> down to that element: per level all matching CSS rules from
/// <see cref="CssCascadeResolver"/>, their specificity, overridden properties and what overrides them.
/// </summary>
public sealed class LiveCssPanelViewModel : ViewModelBase
{
    private string _elementDescription = string.Empty;

    /// <summary>Short element description (e.g. <c>p.note#foo</c>) for the window header.</summary>
    public string ElementDescription
    {
        get => _elementDescription;
        private set => SetProperty(ref _elementDescription, value);
    }

    /// <summary>The hierarchy levels, from <c>body</c> (first) down to the element under the caret (last).</summary>
    public ObservableCollection<LiveCssLevelViewModel> Levels { get; } = new();

    /// <summary>
    /// Replaces the panel content with the result for the newly chosen element.
    /// <paramref name="resolveBookText"/> supplies the content of a file (to compute line numbers from offsets)
    /// by its book path — empty when the file is not available.
    /// </summary>
    public void Update(CssCascadeResult result, Func<string, string> resolveBookText, Action<string, int> jumpTo)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(resolveBookText);
        ArgumentNullException.ThrowIfNull(jumpTo);

        // Each file's text is read once per update, however many rules and annotations point into it.
        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        string CachedText(string bookPath)
        {
            if (!texts.TryGetValue(bookPath, out string? text))
            {
                text = resolveBookText(bookPath);
                texts[bookPath] = text;
            }

            return text;
        }

        ElementDescription = result.ElementDescription;
        Levels.Clear();
        foreach (CssCascadeLevel level in result.Levels)
        {
            Levels.Add(new LiveCssLevelViewModel(level, CachedText, jumpTo));
        }
    }
}
