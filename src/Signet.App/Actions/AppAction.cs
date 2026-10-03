using System;
using System.Text;
using System.Windows.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Signet.App.Actions;

/// <summary>
/// A single application action. It is both an <see cref="ICommand"/> (bound to menus, toolbars
/// and shortcuts) and an observable object (menus/toolbars react to changes of
/// <see cref="IsEnabled"/> and the shortcut).
/// </summary>
/// <remarks>
/// Executing the action raises the <see cref="Invoked"/> event. Dispatch is handled by
/// <see cref="AppActionRegistry"/>: if a handler is attached for the identifier, it is invoked;
/// otherwise "not implemented" is shown in the status bar.
/// </remarks>
public sealed partial class AppAction : ObservableObject, ICommand
{
    internal AppAction(AppActionDescriptor descriptor)
    {
        Descriptor = descriptor;
        _defaultText = ActionTexts.Text(descriptor);
        _categoryDisplayName = ActionTexts.Category(descriptor.Category);
        _text = ConvertMnemonics(_defaultText);
    }

    /// <summary>
    /// Recomputes the texts after a UI language change and resets <see cref="Text"/> to the default
    /// text (overrides, e.g. a clip name in a slot, are set again by their owner).
    /// </summary>
    public void RefreshLocalizedTexts()
    {
        DefaultText = ActionTexts.Text(Descriptor);
        CategoryDisplayName = ActionTexts.Category(Descriptor.Category);
        Text = ConvertMnemonics(DefaultText);
    }

    /// <summary>
    /// Action text in the UI language, with an <c>&amp;</c> mnemonic (translation of
    /// <see cref="AppActionDescriptor.Text"/>). <see cref="Text"/> may temporarily replace it
    /// (e.g. the name of a clip assigned to a slot).
    /// </summary>
    public string DefaultText
    {
        get => _defaultText;
        private set => SetProperty(ref _defaultText, value);
    }

    private string _defaultText;
    private string _categoryDisplayName;

    /// <summary>Category in the UI language — group header in the shortcut editor and next to toolbars.</summary>
    public string CategoryDisplayName
    {
        get => _categoryDisplayName;
        private set => SetProperty(ref _categoryDisplayName, value);
    }

    /// <summary>Static action description (id, source text, icon, category).</summary>
    public AppActionDescriptor Descriptor { get; }

    /// <summary>Action identifier (see <see cref="AppActionIds"/>).</summary>
    public string Id => Descriptor.Id;

    /// <summary>Action category (English main menu name — a group identifier, not for display).</summary>
    public string Category => Descriptor.Category;

    /// <summary>Icon resource key (without extension) or <see langword="null"/>.</summary>
    public string? IconKey => Descriptor.IconKey;

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>Raised on every execution of the action (as long as <see cref="IsEnabled"/>).</summary>
    public event EventHandler? Invoked;

    /// <summary>Displayed text (with an Avalonia-style mnemonic underscore).</summary>
    [ObservableProperty]
    private string _text;

    /// <summary>Whether the action is currently enabled.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// Whether the action is checkable (rendered in the menu as an item with a check mark — heading group,
    /// "Preserve Existing Attributes", panel toggles). Set once during configuration.
    /// </summary>
    [ObservableProperty]
    private bool _isCheckable;

    /// <summary>Checked state of the item (for <see cref="IsCheckable"/>).</summary>
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>
    /// Icon appearance on the toolbar and in the menu (normal / grayed out / red), independent of
    /// <see cref="IsEnabled"/>; e.g. "Save" is gray without changes and red when there are changes.
    /// </summary>
    [ObservableProperty]
    private ActionIconState _iconState;

    /// <summary>Shortcut text shown in the menu (e.g. <c>Ctrl+S</c>); empty when none.</summary>
    [ObservableProperty]
    private string _inputGestureText = string.Empty;

    /// <summary>Current keyboard shortcut (from <c>KeyboardShortcutManager</c>) or <see langword="null"/>.</summary>
    [ObservableProperty]
    private KeyGesture? _gesture;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => IsEnabled;

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (IsEnabled)
        {
            Invoked?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Manually raises <see cref="ICommand.CanExecuteChanged"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    partial void OnIsEnabledChanged(bool value) => RaiseCanExecuteChanged();

    /// <summary>Converts <c>&amp;</c> mnemonics to Avalonia notation (<c>_</c>).</summary>
    internal static string ConvertMnemonics(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder sb = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '&' && i + 1 < text.Length && text[i + 1] == '&')
            {
                sb.Append('&');
                i++;
            }
            else if (c == '&')
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
