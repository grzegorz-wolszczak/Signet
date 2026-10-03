using System.Threading;
using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Styling;
using Nwv = NativeWebView.Core;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Core.Misc;

namespace Signet.App.Views;

/// <summary>
/// The preview panel view: a native <see cref="NativeWebView.Controls.NativeWebView"/> control
/// (WebView2 / WebKitGTK / WKWebView) driven by events from <see cref="PreviewViewModel"/>.
/// Backend initialization is resilient to environments without a working engine (headless, no
/// WebKitGTK) — the panel then shows a fallback message instead of crashing.
/// </summary>
public partial class PreviewView : UserControl
{
    // A script injected when each document is created: on click it sends the host
    // "signet-loc:{offset}" of the nearest ancestor with a data-signet-loc attribute (Preview → Code View).
    private const string ClickBridgeScript =
        "(function(){if(window.__signetPreviewBridge)return;window.__signetPreviewBridge=true;"
        + "document.addEventListener('click',function(e){var el=e.target;"
        + "while(el&&el.nodeType===1&&!el.hasAttribute('data-signet-loc'))el=el.parentElement;"
        + "if(el&&el.nodeType===1&&el.hasAttribute('data-signet-loc')){try{"
        + "window.chrome.webview.postMessage('signet-loc:'+el.getAttribute('data-signet-loc'));}catch(x){}}"
        + "},true);})();";

    // After this time without a real frame the preview is revealed anyway (e.g. when the backend
    // never delivers non-synthetic frames) — better to show something than an empty panel.
    private static readonly TimeSpan RevealFallbackDelay = TimeSpan.FromSeconds(3);

    private PreviewViewModel? _boundViewModel;
    private bool _revealed;
    private bool _initStarted;
    private bool _initialized;
    private bool _initFailed;
    private Uri? _pendingUrl;
    private string? _customCssUri;

    /// <summary>Initializes the view.</summary>
    public PreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // The startup script must be registered before the backend is initialized.
        try
        {
            WebView.InstanceConfiguration.DocumentStartScripts.Add(
                new Nwv.NativeWebViewDocumentStartScript(ClickBridgeScript));
        }
        catch (Exception)
        {
            // A backend without startup script support — Preview → Code View sync is inactive.
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.NavigateRequested -= OnNavigateRequested;
            _boundViewModel.ReloadRequested -= OnReloadRequested;
            _boundViewModel.ZoomChanged -= OnZoomChanged;
            _boundViewModel.ScrollToLocRequested -= OnScrollToLocRequested;
            _boundViewModel.PrintRequested -= OnPrintRequested;
            _boundViewModel.PrintToPdfRequested -= OnPrintToPdfRequested;
            _boundViewModel.InspectorRequested -= OnInspectorRequested;
            _boundViewModel.SelectAllRequested -= OnSelectAllRequested;
            _boundViewModel.CopyRequested -= OnCopyRequested;
            _boundViewModel.CustomCssRequested -= OnCustomCssRequested;
        }

        _boundViewModel = DataContext as PreviewViewModel;

        if (_boundViewModel is not null)
        {
            _boundViewModel.NavigateRequested += OnNavigateRequested;
            _boundViewModel.ReloadRequested += OnReloadRequested;
            _boundViewModel.ZoomChanged += OnZoomChanged;
            _boundViewModel.ScrollToLocRequested += OnScrollToLocRequested;
            _boundViewModel.PrintRequested += OnPrintRequested;
            _boundViewModel.PrintToPdfRequested += OnPrintToPdfRequested;
            _boundViewModel.InspectorRequested += OnInspectorRequested;
            _boundViewModel.SelectAllRequested += OnSelectAllRequested;
            _boundViewModel.CopyRequested += OnCopyRequested;
            _boundViewModel.CustomCssRequested += OnCustomCssRequested;

            Uri? current = _boundViewModel.CurrentUrlOrNull();
            if (current is not null)
            {
                OnNavigateRequested(_boundViewModel, current);
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartWebViewInitialization();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        try
        {
            WebView.RenderFrameCaptured -= OnRenderFrameCaptured;
            WebView.NavigationStarted -= OnNavigationStarted;
            WebView.NavigationCompleted -= OnNavigationCompleted;
            WebView.NewWindowRequested -= OnNewWindowRequested;
            WebView.WebMessageReceived -= OnWebMessageReceived;
            WebView.Dispose();
        }
        catch (Exception)
        {
            // Releasing native resources on close — errors are irrelevant.
        }
    }

    private async void StartWebViewInitialization()
    {
        if (_initStarted || Design.IsDesignMode)
        {
            return;
        }

        _initStarted = true;

        try
        {
            Nwv.NativeWebViewRuntime.EnsureCurrentPlatformRegistered();

            WebView.RenderFrameCaptured += OnRenderFrameCaptured;
            DispatcherTimer.RunOnce(RevealWebView, RevealFallbackDelay);
            WebView.NavigationStarted += OnNavigationStarted;
            WebView.NavigationCompleted += OnNavigationCompleted;
            WebView.NewWindowRequested += OnNewWindowRequested;
            WebView.WebMessageReceived += OnWebMessageReceived;

            await WebView.InitializeAsync().ConfigureAwait(true);

            _initialized = true;

            if (_pendingUrl is not null)
            {
                Navigate(_pendingUrl);
                _pendingUrl = null;
            }

            if (_boundViewModel is not null)
            {
                WebView.SetZoomFactor(_boundViewModel.ZoomFactor);
            }
        }
        catch (Exception ex)
        {
            _initFailed = true;
            ShowFallback(
                Strings.Format("Preview_EngineUnavailable", ex.GetType().Name, ex.Message));
        }
    }

    // Before WebView2 delivers its first image, GpuSurface mode displays a synthetic test frame
    // (a colored pattern) — at application start the preview panel used to "flash" for a moment. The control
    // starts with Opacity=0 and is revealed only on the first real frame.
    private void OnRenderFrameCaptured(object? sender, Nwv.NativeWebViewRenderFrameCapturedEventArgs e)
    {
        if (!e.Frame.IsSynthetic)
        {
            Dispatcher.UIThread.Post(RevealWebView);
        }
    }

    private void RevealWebView()
    {
        if (_revealed)
        {
            return;
        }

        _revealed = true;
        WebView.RenderFrameCaptured -= OnRenderFrameCaptured;
        WebView.Opacity = 1;
    }

    private void OnNavigateRequested(object? sender, Uri url)
    {
        if (_initFailed)
        {
            return;
        }

        if (!_initialized)
        {
            _pendingUrl = url;
            return;
        }

        Navigate(url);
    }

    private void Navigate(Uri url)
    {
        try
        {
            WebView.Navigate(url);
        }
        catch (Exception ex)
        {
            ShowFallback(Strings.Format("Preview_LoadFailed", ex.Message));
        }
    }

    private void OnReloadRequested(object? sender, EventArgs e)
    {
        if (_initialized && !_initFailed)
        {
            try
            {
                WebView.Reload();
            }
            catch (Exception)
            {
                // Ignored — the user can try again.
            }
        }
    }

    private void OnZoomChanged(object? sender, double factor)
    {
        if (_initialized && !_initFailed)
        {
            try
            {
                WebView.SetZoomFactor(factor);
            }
            catch (Exception)
            {
                // Ignored.
            }
        }
    }

    private void OnScrollToLocRequested(object? sender, int loc)
    {
        if (!_initialized || _initFailed)
        {
            return;
        }

        // The highlight replaces the previous one (kept in window.__signetPreviewHighlightEl); the style,
        // the color per the application theme and the optional fade-out come from Preferences (PreviewHighlight).
        PreviewHighlight highlight = _boundViewModel?.Highlight ?? PreviewHighlight.Default;
        string js = PreviewHighlightScript.Build(loc, highlight, ActualThemeVariant == ThemeVariant.Dark);

        try
        {
            _ = WebView.ExecuteScriptAsync(js);
        }
        catch (Exception)
        {
            // Scroll-jumping is a convenience — no reaction is acceptable.
        }
    }

    // File → Print — the native, interactive system print
    // dialog (ShowPrintUiAsync), with a built-in page preview.
    private async void OnPrintRequested(object? sender, EventArgs e)
    {
        if (!_initialized || _initFailed)
        {
            return;
        }

        try
        {
            await WebView.ShowPrintUiAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // The print dialog is unavailable in this environment — no reaction.
        }
    }

    // File → Print Preview — exports the current page to a temporary PDF (PrintAsync),
    // which is then opened in the system's default PDF viewer (this WebView library has no
    // "in-browser preview" mode separate from the native print dialog).
    private async void OnPrintToPdfRequested(object? sender, string path)
    {
        if (!_initialized || _initFailed)
        {
            return;
        }

        try
        {
            var settings = new Nwv.NativeWebViewPrintSettings { OutputPath = path, BackgroundsEnabled = true };
            Nwv.NativeWebViewPrintResult result = await WebView.PrintAsync(settings, CancellationToken.None);
            if (result.Status == Nwv.NativeWebViewPrintStatus.Success)
            {
                _boundViewModel?.NotifyPrintPreviewReady(path);
            }
        }
        catch (Exception)
        {
            // PDF export is unavailable in this environment — no reaction.
        }
    }

    // The Inspector is a separate window
    // (here: the system's native DevTools window), not a dockable panel.
    private void OnInspectorRequested(object? sender, EventArgs e)
    {
        if (!_initialized || _initFailed)
        {
            return;
        }

        try
        {
            WebView.IsDevToolsEnabled = true;
            WebView.OpenDevToolsWindow();
        }
        catch (Exception)
        {
            // DevTools are unavailable in this environment (e.g. WebKitGTK built without devtools).
        }
    }

    // The "Select All"/"Copy" actions (Edit menu, when Preview has focus).
    // document.execCommand is deprecated in the specification, but it is still the only way to
    // trigger native page selection/copying from a script in the WebView2/WebKitGTK/WKWebView
    // engines without the Clipboard API, which requires async per-platform permissions.
    private void OnSelectAllRequested(object? sender, EventArgs e)
    {
        if (!_initialized || _initFailed)
        {
            return;
        }

        try
        {
            _ = WebView.ExecuteScriptAsync(
                "(function(){var s=window.getSelection();s.removeAllRanges();"
                + "var r=document.createRange();r.selectNodeContents(document.body);s.addRange(r);})();");
        }
        catch (Exception)
        {
            // Selection is a convenience — no reaction is acceptable.
        }
    }

    private void OnCopyRequested(object? sender, EventArgs e)
    {
        if (!_initialized || _initFailed)
        {
            return;
        }

        try
        {
            _ = WebView.ExecuteScriptAsync("document.execCommand('copy');");
        }
        catch (Exception)
        {
            // Copying is a convenience — no reaction is acceptable.
        }
    }

    // "Cycle Custom CSS Files": like ClickBridgeScript/OnScrollToLocRequested, the stylesheet
    // is attached by a script in the loaded page, not by modifying the page content in
    // Signet.Core.Preview.PreviewMirror. It must be re-applied after EVERY navigation
    // (reloading the page removes the previously injected <link>), hence OnNavigationCompleted.
    private const string CustomCssLinkId = "signet-custom-preview-css";

    private void OnCustomCssRequested(object? sender, string? cssPath)
    {
        _customCssUri = cssPath is null ? null : new Uri(cssPath).AbsoluteUri;
        ApplyCustomCss();
    }

    private void OnNavigationCompleted(object? sender, Nwv.NativeWebViewNavigationCompletedEventArgs e)
    {
        ApplyCustomCss();
        _boundViewModel?.NotifyPageLoaded();
    }

    private void ApplyCustomCss()
    {
        if (!_initialized || _initFailed)
        {
            return;
        }

        string js = "(function(){var old=document.getElementById('" + CustomCssLinkId + "');"
            + "if(old)old.remove();"
            + (_customCssUri is null
                ? string.Empty
                : "var l=document.createElement('link');l.id='" + CustomCssLinkId + "';"
                  + "l.rel='stylesheet';l.href='" + _customCssUri + "';"
                  + "document.head.appendChild(l);")
            + "})();";

        try
        {
            _ = WebView.ExecuteScriptAsync(js);
        }
        catch (Exception)
        {
            // Custom CSS is a convenience — no reaction is acceptable.
        }
    }

    private void OnWebMessageReceived(object? sender, Nwv.NativeWebViewMessageReceivedEventArgs e)
    {
        string? message = e.Message;
        if (Dispatcher.UIThread.CheckAccess())
        {
            _boundViewModel?.HandlePreviewMessage(message);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _boundViewModel?.HandlePreviewMessage(message));
        }
    }

    private void OnNavigationStarted(object? sender, Nwv.NativeWebViewNavigationStartedEventArgs e)
    {
        if (_boundViewModel is not null && !_boundViewModel.AllowNavigation(e.Uri))
        {
            e.Cancel = true;
        }
    }

    private void OnNewWindowRequested(object? sender, Nwv.NativeWebViewNewWindowRequestedEventArgs e)
    {
        // The preview does not open new windows; a "target=_blank" link goes to the current view
        // if it points to a publication resource, and is otherwise blocked.
        e.Handled = true;

        if (_initialized && _boundViewModel is not null && _boundViewModel.AllowNavigation(e.Uri) && e.Uri is not null)
        {
            Navigate(e.Uri);
        }
    }

    private void ShowFallback(string message)
    {
        void Apply()
        {
            FallbackText.Text = message;
            FallbackText.IsVisible = true;
            WebView.IsVisible = false;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }
}
