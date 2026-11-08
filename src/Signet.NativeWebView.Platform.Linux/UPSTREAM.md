# Signet.NativeWebView.Platform.Linux — upstream

Fork of the Linux backend (WebKitGTK) of **NativeWebView**, the web view behind Signet's Preview. WebView2/WKWebView
backends stay NuGet packages.

| | |
|---|---|
| Upstream | <https://github.com/wieslawsoltes/NativeWebView>, commit [`1b6d498`](https://github.com/wieslawsoltes/NativeWebView/tree/1b6d498913e7631301cad0c333e04f19ec9228c8) (2026-09-14), source of NuGet **12.0.4.9** |
| License | MIT, © 2026 Wiesław Šoltés — see [`LICENSE`](LICENSE); keep the notice (Signet as a whole is GPL) |

## Imported
- `src/NativeWebView.Platform.Linux` → here, verbatim in the first commit.
- Linux-only tests of `NativeWebView.Core.Tests` → `tests/Signet.NativeWebView.Platform.Linux.Tests/Upstream` (upstream
  `Assert` style). Signet's own tests are in `Fork/` (the host-window test needs X11, skipped without it).

Keep `NativeWebView`, `.Core`, `.Interop` and the Windows/macOS backends on the **same NuGet version** as this fork.
The assembly keeps the name `NativeWebView.Platform.Linux` (`InternalsVisibleTo` from Core; runtime registration).
Upstream style is kept (implicit usings, `DllImport`, no analyzers); Signet code is marked `// Signet:`, larger parts
in `*.Signet.cs` / `LinuxNativeCallbackExceptions.cs`. To update: import new upstream, re-apply the changes below
(check if upstream fixed any), bump all `NativeWebView*` packages together.

## Changes against upstream

1. **GTK host window shown only after embedding.** Upstream `gtk_widget_show_all`'d it as a root-window popup first;
   Cinnamon/Muffin kept compositing it → transparent hole. Now only realized in `CreateHostWindowOnGtkThread`;
   `InitializeRuntimeOnGtkThread` shows it after reparenting, with the web view inside (showing before adding the view
   left it grey).
2. **Events on the UI thread** (`LinuxNativeWebViewBackend.Signet.cs`). WebKit signals arrive on the GTK thread;
   upstream raised events there, so handlers touching Avalonia threw, and the exception unwinding through native
   frames killed the process (`stack smashing detected`).
   - Events are posted to the creating `SynchronizationContext`, in order (direct if already on that thread or no context).
   - Policy decisions (`NavigationStarted.Cancel`, `NewWindowRequested.Handled`) are deferred: `decide-policy` keeps the
     `WebKitPolicyDecision`, returns `TRUE`; `use`/`ignore` is called on the GTK thread after the UI handler (also if it threw).
   - Still synchronous on the GTK thread: `ContextMenuRequested`, `WebResourceRequested`; stub/test paths and
     `CoreWebView2EnvironmentRequested`/`ControllerOptionsRequested` stay on the calling thread. Signet uses none.
3. **No managed exception through native frames.** Signal handlers go through `GuardCallback`; the idle source catches
   too. Caught exceptions → `LinuxNativeCallbackExceptions.Report` → posted to the UI thread as `Caught` (Signet's
   `PreviewView` logs and shows a Notifications warning), else rethrown there; stderr without a UI thread.
4. **JS callback delegate kept alive:** `OnJavaScriptFinished` is a `static readonly` field (a method group got collected).
5. **UTF-8 default charset.** WebKitGTK defaults to ISO-8859-1; the preview mirror serves UTF-8 `.html` via `file://`
   without `<meta charset>`, so Polish letters broke. `LinuxNativeWebViewBackend.Charset.Signet.cs` sets
   `default-charset=utf-8` in `InitializeRuntimeOnGtkThread`.
6. **Don't free a signal's `GError`.** `load-failed` / download `failed` handlers freed the error (transfer none);
   WebKit's own handler then read freed memory → `SIGSEGV` whenever a Preview page failed to load. Handlers use
   `GetErrorMessage`; `GetErrorMessageAndFree` stays only for `out GError` of `*_finish`. Test: `LoadFailedTests`.

From upstream 12.0.4.9: `gdk_x11_window_set_frame_sync_enabled(gdkWindow, false)` for the embedded window (otherwise the
view stayed blank).

## Consequences for users of it
- `NativeWebView` event handlers run on the UI thread on every platform. Never block the UI thread waiting for the GTK
  thread in one (deferred `decide-policy`).
- A navigation decided in `NavigationStarted` applies after the handler returns, asynchronously.
- Never pass a method group/lambda to native code that keeps it; hold the delegate in a field or `GCHandle`.
- Never let a managed exception leave a native callback; use `LinuxNativeCallbackExceptions.Report`.

## Diagnosing
- **Window tree (X11):** `xwininfo -root -tree`, `xwininfo -id <id>`; `xwd -id <id> | convert xwd:- png:-`.
- **Native crash:** `coredumpctl list` / `coredumpctl debug <pid>`; managed frames via
  `DOTNET_DbgEnableMiniDump=1 DOTNET_EnableCrashReport=1 DOTNET_DbgMiniDumpName=<path>` (→ `<path>.crashreport.json`).
- **"stack smashing detected" is not always a managed exception.** An abort in `SwitchStackAndExecuteHandler`
  (`libcoreclr.so`) is the runtime handling a native `SIGSEGV`. Find `__restore_rt` on the stack (`x/6000a $sp`); the
  `ucontext_t` follows at +8, `gregs` at +40 (`REG_RIP` 16, `REG_RSP` 15, `REG_TRAPNO` 20); callers are above that
  `RSP`. WebKitGTK debug info: `https://debuginfod.ubuntu.com/buildid/<build id>/debuginfo` (`readelf -n`), then
  `addr2line -f -C -i`. This found change 6.
- **WebKitGTK alone:** minimal GTK3 + `WebKit2 4.1` Python `gi` window shows whether the engine renders at all.
- `WEBKIT_DISABLE_DMABUF_RENDERER=1` / `WEBKIT_DISABLE_COMPOSITING_MODE=1` did not matter; verify a variable reached
  the process via `/proc/<pid>/environ`.
