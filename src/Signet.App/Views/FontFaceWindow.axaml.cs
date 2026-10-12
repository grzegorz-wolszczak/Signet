using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Signet.App.ViewModels;

namespace Signet.App.Views;

/// <summary>
/// The modal "Add @font-face rules" dialog (<see cref="FontFaceDialogViewModel"/>): "Add" writes the checked rules,
/// "Skip" leaves the font files in the book without rules.
/// </summary>
public partial class FontFaceWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public FontFaceWindow()
    {
        InitializeComponent();
        AddButton.Click += (_, _) => Close((DataContext as FontFaceDialogViewModel)?.BuildResult());
        SkipButton.Click += (_, _) => Close(null);
    }

    /// <summary>Shows the dialog; returns the decision, or <c>null</c> when the user skipped.</summary>
    public static Task<FontFaceDialogResult?> ShowAsync(Window owner, FontFaceDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return new FontFaceWindow { DataContext = viewModel }.ShowDialog<FontFaceDialogResult?>(owner);
    }
}
