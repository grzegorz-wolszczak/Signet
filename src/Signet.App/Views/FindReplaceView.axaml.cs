using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.ViewModels;

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
        FindBox.AddHandler(KeyDownEvent, OnFindBoxKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
        ReplaceBox.AddHandler(KeyDownEvent, OnReplaceBoxKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
        FindBoxHistoryButton.Click += (_, _) => OpenHistory(FindBox);
        ReplaceBoxHistoryButton.Click += (_, _) => OpenHistory(ReplaceBox);
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

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && DataContext is FindReplaceViewModel vm)
        {
            vm.FindNext();
            e.Handled = true;
        }
    }

    private void OnReplaceBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && DataContext is FindReplaceViewModel vm)
        {
            vm.ReplaceFind();
            e.Handled = true;
        }
    }
}
