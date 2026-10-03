using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Parsers;

namespace Signet.App.ViewModels;

/// <summary>
/// A single CSS declaration in the Live CSS panel — <see cref="Text"/> is ready for display,
/// <see cref="IsOverridden"/> drives the strikethrough in the view.
/// </summary>
public sealed class LiveCssDeclarationViewModel
{
    internal LiveCssDeclarationViewModel(CssMatchedDeclaration declaration)
    {
        Text = declaration.IsImportant
            ? $"{declaration.Property}: {declaration.Value} !important;"
            : $"{declaration.Property}: {declaration.Value};";
        IsOverridden = declaration.IsOverridden;
    }

    /// <summary>Declaration text to display (<c>property: value;</c>).</summary>
    public string Text { get; }

    /// <summary>Whether this declaration is overridden by another rule with a higher cascade priority.</summary>
    public bool IsOverridden { get; }
}

/// <summary>A single CSS rule matched to the element, with a clickable jump to its declaration.</summary>
public sealed class LiveCssRuleViewModel
{
    internal LiveCssRuleViewModel(CssMatchedRule rule, string bookText, Action<string, int> jumpTo)
    {
        Header = rule.IsInlineStyle
            ? Strings.Get("LiveCssPanelWindow_Inline")
            : FormatHeader(rule, bookText);
        Declarations = new ObservableCollection<LiveCssDeclarationViewModel>(
            rule.Declarations.Select(d => new LiveCssDeclarationViewModel(d)));
        JumpCommand = new RelayCommand(() => jumpTo(rule.BookPath, rule.Offset));
    }

    /// <summary>Rule header: selector, file:line, specificity.</summary>
    public string Header { get; }

    /// <summary>The rule's declarations, with the cascade result per property.</summary>
    public ObservableCollection<LiveCssDeclarationViewModel> Declarations { get; }

    /// <summary>Opens (or activates) the tab with this rule and places the caret on its selector.</summary>
    public RelayCommand JumpCommand { get; }

    private static string FormatHeader(CssMatchedRule rule, string ruleFileText)
    {
        int line = LineOf(ruleFileText, rule.Offset);
        string shortPath = rule.BookPath.Contains('/', StringComparison.Ordinal)
            ? rule.BookPath[(rule.BookPath.LastIndexOf('/') + 1)..]
            : rule.BookPath;
        string specificity = string.Create(
            CultureInfo.InvariantCulture,
            $"{rule.Specificity.Ids},{rule.Specificity.Classes},{rule.Specificity.Types}");
        return $"{rule.SelectorText}  —  {shortPath}:{line}  —  {Strings.Get("LiveCssPanelWindow_Specificity")} {specificity}";
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
/// (the "Live CSS Panel" action, <see cref="MainWindowViewModel.TryResolveLiveCssPanel"/>
/// for the caret of the active Code View tab) shows all matching CSS rules from
/// <see cref="CssCascadeResolver"/>, their specificity and overridden properties.
/// </summary>
public sealed class LiveCssPanelViewModel : ViewModelBase
{
    private string _elementDescription = string.Empty;
    private bool _hasRules;

    /// <summary>Short element description (e.g. <c>p.note#foo</c>) for the window header.</summary>
    public string ElementDescription
    {
        get => _elementDescription;
        private set => SetProperty(ref _elementDescription, value);
    }

    /// <summary>Whether there is at least one matching rule (controls the visibility of the "no rules" message).</summary>
    public bool HasRules
    {
        get => _hasRules;
        private set => SetProperty(ref _hasRules, value);
    }

    /// <summary>Matching CSS rules, in source cascade order (inline style last).</summary>
    public ObservableCollection<LiveCssRuleViewModel> Rules { get; } = new();

    /// <summary>
    /// Replaces the panel content with the result for the newly chosen element.
    /// <paramref name="resolveBookText"/> supplies the content of the rule's file (to compute the line
    /// number from the offset) by its book path — <c>null</c>/empty when the file is not available.
    /// </summary>
    public void Update(CssCascadeResult result, Func<string, string> resolveBookText, Action<string, int> jumpTo)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(resolveBookText);
        ArgumentNullException.ThrowIfNull(jumpTo);

        ElementDescription = result.ElementDescription;
        Rules.Clear();
        foreach (CssMatchedRule rule in result.MatchedRules)
        {
            string bookText = rule.IsInlineStyle ? string.Empty : resolveBookText(rule.BookPath);
            Rules.Add(new LiveCssRuleViewModel(rule, bookText, jumpTo));
        }

        HasRules = Rules.Count > 0;
    }
}
