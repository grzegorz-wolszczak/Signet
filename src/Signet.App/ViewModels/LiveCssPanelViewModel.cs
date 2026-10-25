using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Misc;
using Signet.Core.Parsers;

namespace Signet.App.ViewModels;

/// <summary>
/// A single CSS declaration in the Live CSS panel — <see cref="Text"/> is ready for display,
/// <see cref="IsOverridden"/> drives the strikethrough and <see cref="IsDimmed"/> the dimming in the view;
/// <see cref="OverriddenByText"/> says which rule wins instead.
/// </summary>
public sealed class LiveCssDeclarationViewModel
{
    internal LiveCssDeclarationViewModel(
        CssMatchedDeclaration declaration, Func<string, string> resolveBookText, Action<string, int> jumpTo)
    {
        Text = declaration.IsImportant
            ? $"{declaration.Property}: {declaration.Value} !important;"
            : $"{declaration.Property}: {declaration.Value};";
        IsOverridden = declaration.IsOverridden;
        IsDimmed = !declaration.AffectsElement;
        if (declaration.OverriddenBy is { } source)
        {
            OverriddenByText = Strings.Format("LiveCssPanelWindow_OverriddenBy", LiveCssFormat.Source(source, resolveBookText));
            JumpCommand = new RelayCommand(() => jumpTo(source.BookPath, source.Offset));
        }
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
    public string OverriddenByText { get; } = string.Empty;

    /// <summary>Whether <see cref="OverriddenByText"/> is shown.</summary>
    public bool HasOverriddenBy => OverriddenByText.Length > 0;

    /// <summary>
    /// Opens the rule that wins instead and places the caret on its selector — the annotation says where it is, so
    /// it takes you there. <c>null</c> when the declaration is not overridden.
    /// </summary>
    public RelayCommand? JumpCommand { get; }
}

/// <summary>
/// One inherited property in effect on the inspected element ("Show inherited"): the declaration as it reads, plus
/// the ancestor it comes from and where that rule is.
/// </summary>
public sealed class LiveCssInheritedViewModel
{
    internal LiveCssInheritedViewModel(
        CssInheritedDeclaration inherited, Func<string, string> resolveBookText, Action<string, int> jumpTo)
    {
        JumpCommand = new RelayCommand(() => jumpTo(inherited.Source.BookPath, inherited.Source.Offset));
        Text = inherited.IsImportant
            ? $"{inherited.Property}: {inherited.Value} !important;"
            : $"{inherited.Property}: {inherited.Value};";
        SourceText = Strings.Format(
            "LiveCssPanelWindow_InheritedFrom",
            $"{inherited.ElementDescription} \u2014 {LiveCssFormat.Location(inherited.Source.BookPath, inherited.Source.Offset, resolveBookText)}");
    }

    /// <summary>Declaration text to display (<c>property: value;</c>).</summary>
    public string Text { get; }

    /// <summary>"(inherited from div.a — style.css:12)".</summary>
    public string SourceText { get; }

    /// <summary>Opens the rule of the ancestor that gives the value and places the caret on its selector.</summary>
    public RelayCommand JumpCommand { get; }
}

/// <summary>A single CSS rule matched to the element, with a clickable jump to its declaration.</summary>
public sealed class LiveCssRuleViewModel
{
    internal LiveCssRuleViewModel(CssMatchedRule rule, Func<string, string> resolveBookText, Action<string, int> jumpTo)
    {
        Header = rule.IsInlineStyle
            ? Strings.Get("LiveCssPanelWindow_Inline")
            : FormatHeader(rule, resolveBookText);
        SelectorText = rule.IsInlineStyle ? Strings.Get("LiveCssPanelWindow_Inline") : rule.SelectorText;
        HeaderSuffix = rule.IsInlineStyle ? string.Empty : Header[SelectorText.Length..];
        Declarations = new ObservableCollection<LiveCssDeclarationViewModel>(
            rule.Declarations.Select(d => new LiveCssDeclarationViewModel(d, resolveBookText, jumpTo)));
        JumpCommand = new RelayCommand(() => jumpTo(rule.BookPath, rule.Offset));
    }

    /// <summary>Rule header: selector, file:line, specificity.</summary>
    public string Header { get; }

    /// <summary>The selector part of <see cref="Header"/> — shown in the colors of the CSS highlighter.</summary>
    public string SelectorText { get; }

    /// <summary>The rest of <see cref="Header"/> (file:line, specificity) — shown muted, beside the selector.</summary>
    public string HeaderSuffix { get; }

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
public sealed partial class LiveCssLevelViewModel : ObservableObject
{
    internal LiveCssLevelViewModel(
        CssCascadeLevel level,
        LiveCssPanelViewModel panel,
        bool isFirst,
        Func<string, string> resolveBookText,
        Action<string, int> jumpTo)
    {
        ElementDescription = level.ElementDescription;
        Header = level.IsInspectedElement
            ? $"{level.ElementDescription}  {Strings.Get("LiveCssPanelWindow_InspectedElement")}"
            : level.ElementDescription;
        IsInspectedElement = level.IsInspectedElement;
        Panel = panel;
        ShowSeparator = !isFirst;
        Rules = new ObservableCollection<LiveCssRuleViewModel>(
            level.MatchedRules.Select(r => new LiveCssRuleViewModel(r, resolveBookText, jumpTo)));
    }

    /// <summary>Element description (e.g. <c>div.a</c>), marked for the element under the caret.</summary>
    public string Header { get; }

    /// <summary>The element description alone — the key the panel remembers the collapsed levels by.</summary>
    public string ElementDescription { get; }

    /// <summary>Whether this is the element under the caret (the last level).</summary>
    public bool IsInspectedElement { get; }

    /// <summary>The panel this level belongs to — the view binds the "Show inherited" box to it.</summary>
    public LiveCssPanelViewModel Panel { get; }

    /// <summary>Whether a line is drawn above this level (every level but the first) to tell the levels apart.</summary>
    public bool ShowSeparator { get; }

    /// <summary>
    /// Whether the rules of this level are shown. The element under the caret is what the panel is about, so it
    /// cannot be collapsed; its ancestors can (<see cref="CanCollapse"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    private bool _isExpanded = true;

    /// <summary>Whether this level can be collapsed — every level but the element under the caret.</summary>
    public bool CanCollapse => !IsInspectedElement;

    /// <summary>The triangle of the collapse button, pointing down when the level is open.</summary>
    public string ExpandGlyph => IsExpanded ? "\u25be" : "\u25b8";

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
/// (<see cref="MainWindowViewModel.TryResolveLiveCssPanel"/> for the caret of the active Code Editor tab)
/// shows the element hierarchy from <c>body</c> down to that element: per level all matching CSS rules from
/// <see cref="CssCascadeResolver"/>, their specificity, overridden properties and what overrides them.
/// </summary>
public sealed class LiveCssPanelViewModel : ViewModelBase
{
    private readonly SettingsStore? _settings;

    // The levels the user collapsed, by element description: the panel is rebuilt on every caret move, and a level
    // that was folded away has to stay folded while the window is open.
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    private string _elementDescription = string.Empty;
    private bool _showInherited;

    /// <summary>Creates the panel; <paramref name="settings"/> keeps "Show inherited" between runs (<c>null</c>: not kept).</summary>
    public LiveCssPanelViewModel(SettingsStore? settings = null)
    {
        _settings = settings;
        _showInherited = settings?.LiveCssShowInherited ?? false;
    }

    /// <summary>Short element description (e.g. <c>p.note#foo</c>) for the window header.</summary>
    public string ElementDescription
    {
        get => _elementDescription;
        private set => SetProperty(ref _elementDescription, value);
    }

    /// <summary>The hierarchy levels, from <c>body</c> (first) down to the element under the caret (last).</summary>
    public ObservableCollection<LiveCssLevelViewModel> Levels { get; } = new();

    /// <summary>
    /// The properties the element under the caret inherits from its ancestors without setting them itself — shown
    /// above its own rules when <see cref="ShowInherited"/>.
    /// </summary>
    public ObservableCollection<LiveCssInheritedViewModel> Inherited { get; } = new();

    /// <summary>Whether there is anything inherited to show.</summary>
    public bool HasInherited => Inherited.Count > 0;

    /// <summary>Whether the box of inherited properties is shown (asked for, and there is something in it).</summary>
    public bool ShowInheritedBox => ShowInherited && HasInherited;

    /// <summary>
    /// Whether the inherited properties are listed ("Show inherited", the check box under the header of the element
    /// under the caret). Kept in the settings, so the panel opens the way it was left.
    /// </summary>
    public bool ShowInherited
    {
        get => _showInherited;
        set
        {
            if (!SetProperty(ref _showInherited, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ShowInheritedBox));
            if (_settings is not null)
            {
                _settings.LiveCssShowInherited = value;
                _settings.Save();
            }
        }
    }

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
        foreach (LiveCssLevelViewModel old in Levels)
        {
            old.PropertyChanged -= OnLevelPropertyChanged;
        }

        Levels.Clear();
        for (int i = 0; i < result.Levels.Count; i++)
        {
            LiveCssLevelViewModel level = new(result.Levels[i], this, i == 0, CachedText, jumpTo)
            {
                IsExpanded = !_collapsed.Contains(result.Levels[i].ElementDescription),
            };
            level.PropertyChanged += OnLevelPropertyChanged;
            Levels.Add(level);
        }

        Inherited.Clear();
        foreach (CssInheritedDeclaration inherited in result.Inherited)
        {
            Inherited.Add(new LiveCssInheritedViewModel(inherited, CachedText, jumpTo));
        }

        OnPropertyChanged(nameof(HasInherited));
        OnPropertyChanged(nameof(ShowInheritedBox));
    }

    private void OnLevelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LiveCssLevelViewModel.IsExpanded) || sender is not LiveCssLevelViewModel level)
        {
            return;
        }

        if (level.IsExpanded)
        {
            _collapsed.Remove(level.ElementDescription);
        }
        else
        {
            _collapsed.Add(level.ElementDescription);
        }
    }
}
