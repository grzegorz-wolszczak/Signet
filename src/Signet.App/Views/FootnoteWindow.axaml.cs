using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Signet.App.CodeView;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Nwv = NativeWebView.Core;

namespace Signet.App.Views;

/// <summary>
/// The footnote window from the Code View context menu, for adding a footnote at the caret or editing the footnote
/// under it: the text around the reference, the text of the footnote and its placement (local or global), with a live
/// preview — the markup Signet writes ("Code") and how it looks with the file's styles ("Appearance", a web view that is
/// created when the tab is first shown and gets the page a moment after the last change). An edited footnote can be
/// removed ("Remove", after a confirmation unless it was turned off). OK is not the default button, so Enter makes a new
/// line in the text.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The web view and the folder of its page are disposed when the dialog closes.")]
public partial class FootnoteWindow : Window
{
    // The pause in typing after which the "Appearance" page is refreshed.
    private static readonly TimeSpan AppearanceDelay = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherTimer _appearanceTimer;
    private FootnoteViewModel? _viewModel;
    private NativeWebView.Controls.NativeWebView? _webView;
    private bool _webViewReady;
    private bool _webViewFailed;
    private TempFolder? _pageFolder;

    /// <summary>Initializes the window.</summary>
    public FootnoteWindow()
        : this(CodeViewAppearance.Default)
    {
    }

    /// <summary>Initializes the window with the font of Code View and the colors of the theme for the "Code" preview.</summary>
    public FootnoteWindow(CodeViewAppearance appearance)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(appearance);
        if (CodeViewTextMate.CreateColorizer(CodeViewSyntax.Html, extended: false, linkExists: null, AppTheme.Current) is { } colorizer)
        {
            PreviewCodeEditor.TextArea.TextView.LineTransformers.Add(colorizer);
        }

        EditorTheming.Apply(PreviewCodeEditor, AppTheme.Current);

        if (!string.IsNullOrWhiteSpace(appearance.FontFamily))
        {
            PreviewCodeEditor.FontFamily = new Avalonia.Media.FontFamily(appearance.FontFamily);
        }

        _appearanceTimer = new DispatcherTimer { Interval = AppearanceDelay };
        _appearanceTimer.Tick += (_, _) => ShowAppearance();
        OkButton.Click += (_, _) => Accept();
        RemoveButton.Click += async (_, _) => await RemoveAsync().ConfigureAwait(true);
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) => ContentBox.Focus();
        PreviewTabs.SelectionChanged += (_, _) => OnPreviewTabChanged();
        DataContextChanged += (_, _) => Bind(DataContext as FootnoteViewModel);
        Closed += (_, _) => Release();
    }

    /// <summary>Shows the window; returns what to do once accepted (add, save or remove), or <c>null</c>.</summary>
    public static Task<FootnoteRequest?> AskAsync(Window owner, FootnoteViewModel viewModel) =>
        AskAsync(owner, viewModel, CodeViewAppearance.Default);

    /// <summary>
    /// Shows the window with the Code View font of <paramref name="appearance"/> in the "Code" preview; returns what to
    /// do once accepted (add, save or remove), or <c>null</c>.
    /// </summary>
    public static Task<FootnoteRequest?> AskAsync(Window owner, FootnoteViewModel viewModel, CodeViewAppearance appearance)
    {
        FootnoteWindow window = new(appearance) { DataContext = viewModel };
        return window.ShowDialog<FootnoteRequest?>(owner);
    }

    private void Bind(FootnoteViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        PreviewCodeEditor.Text = _viewModel?.PreviewCode ?? string.Empty;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FootnoteViewModel.PreviewCode))
        {
            PreviewCodeEditor.Text = _viewModel?.PreviewCode ?? string.Empty;
        }
        else if (e.PropertyName == nameof(FootnoteViewModel.PreviewPage) && _webView is not null)
        {
            _appearanceTimer.Stop();
            _appearanceTimer.Start();
        }
    }

    private void OnPreviewTabChanged()
    {
        if (PreviewTabs.SelectedItem != AppearanceTab || _webViewFailed)
        {
            return;
        }

        if (_webView is null)
        {
            StartWebView();
        }
        else
        {
            ShowAppearance();
        }
    }

    // The web view of the "Appearance" tab, created on its first showing (it renders a page written to a file: the
    // library loads only URLs).
    private async void StartWebView()
    {
        _webView = new NativeWebView.Controls.NativeWebView();
        AppearanceHost.Children.Insert(0, _webView);
        try
        {
            Nwv.NativeWebViewRuntime.EnsureCurrentPlatformRegistered();
            await _webView.InitializeAsync().ConfigureAwait(true);
            _webViewReady = true;
            ShowAppearance();
        }
        catch (Exception ex)
        {
            _webViewFailed = true;
            AppearanceHost.Children.Remove(_webView);
            AppearanceMessage.Text = Strings.Format("Preview_EngineUnavailable", ex.GetType().Name, ex.Message);
            AppearanceMessage.IsVisible = true;
        }
    }

    private void ShowAppearance()
    {
        _appearanceTimer.Stop();
        if (!_webViewReady || _webView is null || _viewModel?.PreviewPage is not { } page)
        {
            return;
        }

        try
        {
            _pageFolder ??= new TempFolder();
            string path = Path.Combine(_pageFolder.Path, "footnote-preview.html");
            File.WriteAllText(path, page);
            _webView.Navigate(new Uri(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppearanceMessage.Text = Strings.Format("Preview_LoadFailed", ex.Message);
            AppearanceMessage.IsVisible = true;
        }
    }

    private void Release()
    {
        _appearanceTimer.Stop();
        Bind(null);
        try
        {
            _webView?.Dispose();
        }
        catch (Exception)
        {
            // Releasing native resources of a closed window — errors are irrelevant.
        }

        _pageFolder?.Dispose();
    }

    private void Accept()
    {
        if (DataContext is FootnoteViewModel { CanAccept: true } vm)
        {
            Close(vm.Request());
        }
    }

    private async Task RemoveAsync()
    {
        if (DataContext is not FootnoteViewModel { IsEditing: true } vm)
        {
            return;
        }

        if (vm.ConfirmRemoval)
        {
            (bool confirmed, bool dontAskAgain) = await ConfirmWindow.AskWithDontAskAgainAsync(
                this, Strings.Get("FootnoteWindow_RemoveConfirmTitle"), Strings.Get("FootnoteWindow_RemoveConfirm"),
                Strings.Get("FootnoteWindow_RemoveButton"), destructive: true).ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }

            if (dontAskAgain)
            {
                vm.StopConfirmingRemoval?.Invoke();
            }
        }

        Close(vm.RemoveRequest());
    }
}
