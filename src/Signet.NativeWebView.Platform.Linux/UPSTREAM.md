# Signet.NativeWebView.Platform.Linux — upstream

This project is a fork of the Linux backend of the open-source **NativeWebView** library, the native web view behind
Signet's Preview (WebKitGTK on Linux; WebView2 on Windows and WKWebView on macOS stay NuGet packages).

| | |
|---|---|
| Upstream repository | <https://github.com/wieslawsoltes/NativeWebView> |
| Upstream commit | [`1b6d498913e7631301cad0c333e04f19ec9228c8`](https://github.com/wieslawsoltes/NativeWebView/tree/1b6d498913e7631301cad0c333e04f19ec9228c8) (2026-09-14), the source of the NuGet release **12.0.4.9** |
| License | MIT, Copyright (c) 2026 Wiesław Šoltés — see [`LICENSE`](LICENSE) |

The MIT license is compatible with Signet's GPL v3 or later: the files of this project stay under MIT (the license
notice must be kept), Signet as a whole is distributed under the GPL.

## What was imported

- `src/NativeWebView.Platform.Linux` → `src/Signet.NativeWebView.Platform.Linux` (all sources, verbatim).
- The Linux tests of `tests/NativeWebView.Core.Tests` → `tests/Signet.NativeWebView.Platform.Linux.Tests/Upstream`:
  the tests that exercise only the Linux backend (from `BrowserNavigationPolicyTests`, `DesktopBackendMatrixTests`,
  `ProfileStorageConfigurationTests`, `SnapshotSupportTests`), in their upstream style (xUnit `Assert`). The upstream
  tests that check the Linux backend together with other platforms' backends (Windows, macOS, Android, iOS, Browser)
  are not ported. Signet's own tests of the changes below are in `Fork/` (the host window test needs an X11 display and
  is skipped without one; the window it creates is never shown).

Only the Linux backend is forked. `NativeWebView`, `NativeWebView.Core`, `NativeWebView.Interop` and the Windows /
macOS backends are the NuGet packages of the **same version** (`Directory.Packages.props`): keep them all on the
version this fork is based on. The assembly keeps its upstream name `NativeWebView.Platform.Linux`, because
`NativeWebView.Core` gives its internals to that name (`InternalsVisibleTo`) and the runtime registers the backend by
it; `Properties/AssemblyInfo.cs` also gives the fork's internals to its tests. The code keeps its upstream style (implicit usings, `DllImport`, no analyzers); Signet's own code in the fork is
marked with `// Signet:` comments, its larger parts live in `*.Signet.cs` / `LinuxNativeCallbackExceptions.cs`.

The first commit of this fork imports the files verbatim; every change after it is a regular commit in Signet's
history. To move to a newer upstream: import the new upstream files, re-apply the changes below (check whether
upstream fixed any of them), bump all the `NativeWebView*` packages to the same version.

## Changes against upstream

### 1. The GTK host window is not shown before it is embedded

`CreateHostWindowOnGtkThread` used to `gtk_widget_show_all` the GTK window (an override-redirect popup) right after
creating it — as a window of its own on the root window — and only then reparented it into Avalonia's native control
host. Cinnamon's compositor (Muffin, X11) picked it up as a separate window and kept compositing it: the preview was a
**transparent hole** showing the windows (or, with `_NET_WM_OPAQUE_REGION`, the wallpaper) below Signet. The window is
now only realized there; `InitializeRuntimeOnGtkThread` shows it, with the web view already inside, once it is
reparented. (Showing it right after the reparenting, before the web view was added, left the view grey until the panel
was closed and opened again.)

### 2. Events on the UI thread

WebKitGTK calls the backend's signal handlers on NativeWebView's own GTK thread (`NativeWebView.GTK`,
`LinuxGtkDispatcher`), inside `gtk_main_iteration`. Upstream raises the backend's events (`NavigationStarted`,
`NavigationCompleted`, `WebMessageReceived`, `NewWindowRequested`, …) right there, and neither `NativeWebView.Core`
nor the `NativeWebView` control moves them to the UI thread. On Windows WebView2 raises them on the UI thread, so
Signet's `PreviewView` assumed it; on Linux its handlers ran on the GTK thread, and the first touch of an Avalonia
control threw "Call from invalid thread". That exception then unwound through the native GTK/GLib frames between the
signal and `gtk_main_iteration`, which corrupts the stack: the process died with
`*** stack smashing detected ***` — in Signet on "Close All Tabs" (clearing the preview, navigating and running
scripts at once), not every time.

`LinuxNativeWebViewBackend.Signet.cs`:

- The events raised from the GTK signals are **posted to the `SynchronizationContext` the backend was created on**
  (the `NativeWebView` control creates it on the UI thread), in the order they were raised. Raised on the event thread
  already, or without a context (unit tests), they are raised directly, as upstream.
- The policy decisions answered by an event — `NavigationStarted.Cancel`, `NewWindowRequested.Handled` — are
  **deferred**: the `decide-policy` handler keeps a reference to the `WebKitPolicyDecision`, returns `TRUE`, and
  `webkit_policy_decision_use` / `_ignore` is called on the GTK thread after the handler on the UI thread has decided
  (`use` also when the handler threw).
- Still raised on the GTK thread, synchronously (documented at the call): `ContextMenuRequested` (WebKit builds the menu
  when the signal returns) and `WebResourceRequested` (deferring a response decision would lose WebKit's own handling,
  e.g. a download). The events of the paths without a runtime (the stub used before initialization and in tests) and
  `CoreWebView2EnvironmentRequested` / `ControllerOptionsRequested` (their handlers change the options before they
  are used) stay synchronous on the calling thread. Signet handles none of them.

### 3. No managed exception through the native frames

Every GTK signal handler is registered through `GuardCallback`, and the GTK idle source (`IdleSourceCallback`) catches
what its action throws. A caught exception goes to `LinuxNativeCallbackExceptions.Report`: posted to the UI thread and
raised as `LinuxNativeCallbackExceptions.Caught` (Signet's `PreviewView` logs it as an error and shows a warning in the
Notifications panel — the preview keeps working), or rethrown there as an unhandled exception when nobody handles the
event; written to stderr when there is no UI thread.

### 4. The JavaScript callback delegate is kept alive

`webkit_web_view_run_javascript` was given `OnJavaScriptFinished` as a method group: a temporary delegate the
marshaller kept alive only for the call itself, while WebKit calls it later (a garbage-collected delegate called from
native code crashes the process). It is now a `static readonly` field, like the snapshot and Cairo callbacks.

### 5. UTF-8 as the default encoding

WebKitGTK's `default-charset` setting is ISO-8859-1. Signet's preview mirror writes every text file in UTF-8 and serves
it through `file://`; a chapter with an `.html` extension is then parsed as HTML, where the XML declaration's
`encoding="utf-8"` counts for nothing, and without a `<meta charset>` or a BOM WebKit read it as ISO-8859-1 — every
Polish letter showed as two or three Latin-1 characters (`sÅ‚oneczny`). WebView2 on Windows detects UTF-8 there, so the
same book looked right on Windows (found in November 2026). `LinuxNativeWebViewBackend.Charset.Signet.cs` sets
`default-charset` to `utf-8` once the web view's settings exist (`InitializeRuntimeOnGtkThread`). A page that declares
its encoding keeps it.

### 6. A signal's GError is not freed

The handlers of `load-failed` and of a download's `failed` signal freed the `GError` the signal hands them
(`GetErrorMessageAndFree`). That error belongs to the emitter (transfer none): WebKit frees it itself, and first runs
its own class handler of `load-failed` (`webkitWebViewLoadFail`) after the application's handlers return `FALSE`. That
handler builds its error page with `g_strdup_printf("…%s…", error->message)` — from freed memory, where glibc's tcache
had written its key over `message`: a `SIGSEGV` (general protection fault) in `strlen` on the GTK thread. In Signet it
came every time a page shown in the Preview failed to load — after merging HTML files in the Book Browser with the
Preview open, the shown file left the preview mirror (found in November 2026). The handlers now read the message with
`GetErrorMessage`, which does not free it; `GetErrorMessageAndFree` stays for the errors returned through an
`out GError` parameter (`*_finish`), which belong to the caller. Covered by `LoadFailedTests` (without the fix it ends
the test process).

### From upstream since 12.0.4.8

12.0.4.9 brings `gdk_x11_window_set_frame_sync_enabled(gdkWindow, false)` for the embedded window: an embedded window
is not managed by the window manager, GTK's frame clock waited for frame acknowledgements that never came, and the view
stayed blank (and did not repaint on scrolling or selecting) until it was hidden and shown again.

## Consequences for the code using it

- **Handlers of `NativeWebView` events run on the UI thread on every platform** (Linux: thanks to change 2). Do not
  block the UI thread waiting for the GTK thread inside such a handler — the GTK thread may be waiting for the UI
  thread's decision (deferred `decide-policy`).
- A navigation decided by `NavigationStarted` is applied after the handler returns, asynchronously: the page does not
  start loading before the UI thread has run the handler.
- Never pass a method group or lambda to a native function that keeps it beyond the call — keep the delegate in a field
  (or a `GCHandle`), as all the other callbacks of `LinuxNativeInterop` do.
- Never let a managed exception leave a callback called from native code; catch it and report it
  (`LinuxNativeCallbackExceptions.Report`).

## Diagnosing such problems

- **What the window tree looks like** (X11): `xwininfo -root -tree`, `xwininfo -id <id>` (depth, map state, size);
  `xwd -id <id> | convert xwd:- png:-` shows what a window itself holds.
- **A native crash** (`stack smashing detected`, `SIGSEGV`, `SIGABRT`): `coredumpctl list` / `coredumpctl debug <pid>`
  for the native stacks; for the managed ones run Signet with
  `DOTNET_DbgEnableMiniDump=1 DOTNET_EnableCrashReport=1 DOTNET_DbgMiniDumpName=<path>` — the runtime writes
  `<path>.crashreport.json` with the managed frames of every thread (the full dump is large: put it on a disk with room).
- **"stack smashing detected" is not always a managed exception.** When the abort comes from
  `SwitchStackAndExecuteHandler` in `libcoreclr.so` (symbols: `dotnet-symbol --symbols libcoreclr.so`), the runtime was
  handling a **native signal** (`SIGSEGV`) raised in native code on that thread, and the canary check failed in its
  handler. The faulting place is in the signal frame: find `__restore_rt` on the thread's stack (`x/6000a $sp` in
  `coredumpctl debug`); the `ucontext_t` follows its address + 8, its `gregs` at + 40 (`REG_RIP` 16, `REG_RSP` 15,
  `REG_TRAPNO` 20). The return addresses above that `RSP` give the native callers; WebKitGTK's debug info comes from
  `https://debuginfod.ubuntu.com/buildid/<build id>/debuginfo` (`readelf -n` gives the build id), then `addr2line -f -C -i`.
  This is how change 6 was found; the crash report showed only `RunGtkLoop` and no managed exception.
- **WebKitGTK alone**: a minimal GTK 3 + `WebKit2 4.1` window (Python `gi`) tells whether the engine renders on the
  machine at all, apart from the embedding.
- Environment variables such as `WEBKIT_DISABLE_DMABUF_RENDERER=1` or `WEBKIT_DISABLE_COMPOSITING_MODE=1` did **not**
  matter for the problems above; check that a variable really reached the process (`/proc/<pid>/environ`).
