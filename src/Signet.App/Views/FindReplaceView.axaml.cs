using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Signet.App.ViewModels;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// View of the "Find &amp; Replace" panel. Enter in the Find field runs Find Next,
/// Enter in the Replace field — Replace/Find. The ▾ buttons in the fields expand the history.
/// </summary>
public partial class FindReplaceView : UserControl
{
    /// <summary>Initializes the view.</summary>
    public FindReplaceView()
    {
        InitializeComponent();
        // The handler is attached directly to the AutoCompleteBox: the event source is its internal TextBox
        // (PART_TextBox), not the AutoCompleteBox itself. Enter with the history list open
        // confirms the choice — AutoCompleteBox then marks it as handled, so it does not reach here.
        // The fields take several lines, so their TextBox would insert a line for Enter: the handler runs first (tunnel).
        FindBox.AddHandler(KeyDownEvent, OnFindBoxKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ReplaceBox.AddHandler(KeyDownEvent, OnReplaceBoxKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is FindReplaceViewModel vm)
            {
                vm.TextPrompt = AskTextAsync;
                ApplyFieldFont(vm.FieldFont);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(FindReplaceViewModel.FieldFont))
                    {
                        ApplyFieldFont(vm.FieldFont);
                    }
                };
            }
        };
        // The list of the history exists once the template of the field is applied (after the font was first set).
        foreach (AutoCompleteBox box in new[] { FindBox, ReplaceBox })
        {
            box.TemplateApplied += (_, e) =>
            {
                if (e.NameScope.Find<SelectingItemsControl>("PART_SelectingItemsControl") is { } list)
                {
                    _historyLists[box] = list;
                }

                ApplyFieldFont(_fieldFont);
            };
        }

        FindBoxHistoryButton.Click += (_, _) => OpenHistory(FindBox);
        ReplaceBoxHistoryButton.Click += (_, _) => OpenHistory(ReplaceBox);
    }

    private FindReplaceAppearance _fieldFont = FindReplaceAppearance.Default;
    private readonly System.Collections.Generic.Dictionary<AutoCompleteBox, SelectingItemsControl> _historyLists = new();

    // The font of the Find and Replace fields (Preferences); not set = the interface font. The list of the history that drops
    // down from a field is in a popup of its own, which does not inherit it: it gets the same font.
    private void ApplyFieldFont(FindReplaceAppearance look)
    {
        _fieldFont = look;
        foreach (AutoCompleteBox box in new[] { FindBox, ReplaceBox })
        {
            ClipsLook.SetFont(box, look.FieldFontFamily, look.FieldFontSize, look.FieldFontWeight);
            if (_historyLists.TryGetValue(box, out SelectingItemsControl? list))
            {
                ClipsLook.SetFont(list, look.FieldFontFamily, look.FieldFontSize, look.FieldFontWeight);
            }
        }
    }

    /// <summary>Gives focus to the Find field (the "Find" action, Ctrl+F).</summary>
    public void FocusFindField() => FindBox.Focus();

    // The ▾ button on the right of the field — like a combo box drop-down: shows the whole
    // history (FilterMode="None", so no filtering by the typed text). Clicking ▾ with the
    // list open closes it through the popup's own light dismiss.
    private static void OpenHistory(AutoCompleteBox box)
    {
        box.Focus();
        box.IsDropDownOpen = true;
    }

    private Task<string?> AskTextAsync(string title, string prompt, string initial) =>
        TopLevel.GetTopLevel(this) is Window owner ? TextPromptWindow.AskAsync(owner, title, prompt, initial) : Task.FromResult<string?>(null);

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleEnter(FindBox, e) && DataContext is FindReplaceViewModel vm)
        {
            vm.FindNext();
        }
    }

    private void OnReplaceBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleEnter(ReplaceBox, e) && DataContext is FindReplaceViewModel vm)
        {
            vm.ReplaceFind();
        }
    }

    // Enter alone runs the action (true; unless the history list is open: it confirms the choice); Ctrl+Enter and
    // Shift+Enter add a line to the field (false).
    private static bool HandleEnter(AutoCompleteBox box, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || box.IsDropDownOpen)
        {
            return false;
        }

        e.Handled = true;
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Shift
            && box.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } textBox)
        {
            string text = textBox.Text ?? string.Empty;
            int start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
            int length = Math.Abs(textBox.SelectionEnd - textBox.SelectionStart);
            textBox.Text = text.Remove(start, length).Insert(start, "\n");
            textBox.CaretIndex = start + 1;
            return false;
        }

        return e.KeyModifiers == KeyModifiers.None;
    }
}
