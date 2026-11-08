# Signet

A desktop editor for EPUB 2 and EPUB 3 ebooks, written in C# with [Avalonia](https://avaloniaui.net/) (vibe coded with AI).

## Use at your own risk

Signet is under heavy development; **nothing is guaranteed to work correctly**. Keep a backup of the original book.
It is built for the owner's personal use: no releases, support or roadmap.

## Where it comes from

A sandbox experiment porting **[Sigil](https://github.com/Sigil-Ebook/Sigil)** (C++/Qt6, based on commit
[`654c579`](https://github.com/Sigil-Ebook/Sigil/tree/654c579bb65636ca4fdea1720057a4bc82a71f04)) to .NET with AI.
Some features are modelled on **[calibre](https://github.com/kovidgoyal/calibre)**'s Edit Book, others are original. The
code has drifted from Sigil; this is **not** Sigil and is not affiliated with Sigil or calibre.

## Features unique to Signet
(As far as I know, neither Sigil nor calibre has them.)

- **Highlight of the clicked element in Preview** (Preferences → Preview: background or outline, colour, opacity,
  optional fade).
- **Merge Content:** joins adjacent sibling elements of the same kind
  (`<p>The cat</p> <p>sat on the mat</p>` → `<p>The cat sat on the mat</p>`); the selection only has to start in the
  first and end in the last.
- **Cascade-aware CSS class rename**, from Code Editor (caret on a name in `class="…"`: whole book, or only elements
  with the same nesting, copying style definitions to the new name) or from a stylesheet/`<style>` (caret on a class in
  a selector: only elements using that stylesheet's rules; each defining source is renamed, copied or left alone).
  Sigil and calibre rename everywhere, ignoring the cascade.
- **Cleanup (Tools → Cleanup):** independent **CSS**, **HTML** and **Files** tabs, each planned and applied separately,
  with a full preview, tick/untick per item and double-click to jump to the code.
  - Every step simulates the CSS cascade before/after for each affected element; a change that would alter the look is
    marked ⚠, unticked, with its consequences listed.
  - **CSS:** merges rules with the same selector or properties into the first place where styling stays the same; flags
    risky selector lists (vendor-prefixed, Selectors 4, unparsable); follows `@import`.
  - **HTML** (not Prettify/Mend): collapses nested `<div>`s with identical attributes, removes empty `<p>`/`<span>`/`<div>`,
    unwraps bare `<span>`s, removes `id`s nothing refers to (⚠ if a CSS selector still reaches it). An element whose `id`
    is used is never removed.
- **Remove span** (Code Editor context menu): unwraps the `<span>` at the caret, optionally every identical span in the
  file or book; risky removals use the ⚠ confirmation.
- **Unwrap `<div>`** (context and Edit menus): removes the innermost `<div>` tags around the caret, keeps content one
  level up and shifts indentation; asks if it has an `id`; greyed out with a tooltip when unavailable; no default shortcut.
- **Live CSS Panel** with the element hierarchy from `<body>` to the caret: matching rules per element, overridden
  inherited properties struck through with the winning rule (selector, file, line). May show more than needed.
- **Notifications panel:** history of status bar messages; errors in red (colour in Preferences); unread errors put a red
  dot and count (`[4]`) on the bell tab and status bar.
- **Edition page** (Preferences → Edition page): on the first save of a session Signet adds/refreshes a page with a table
  (your fields, Signet version, revision number growing once per session — a number you type continues the count —, UTC
  save date). You choose its position and whether it gets a TOC entry; a marker lets Signet find and remove it.
- **Switching UI language (Polish/English) without restarting.**

## Sigil features not implemented
- Python plugin framework; automation lists; Python function replacements in Find & Replace (`\F<name>` is literal).
- Index Editor (mark, add, create index).
- Git-based checkpoints (Signet has simpler snapshots).
- Audio/video/PDF viewing (file info and "open externally" only).
- External editors ("Open With", "Edit with external XHTML editor"; the Preferences path is unused).
- Change Reading Order dialog (use Move Up/Down and Sort in the Book Browser).
- Help menu links; file dialog with preview.

## Building
.NET SDK **10.x**:
```bash
dotnet build -c Release
dotnet test  -c Release
dotnet run --project src/Signet.App
```

## Layout
| Project | Role |
|---|---|
| `src/Signet.Core` | domain model (EPUB/OCF/OPF, import/export), no UI |
| `src/Signet.App` | Avalonia app (MVVM, CommunityToolkit.Mvvm) |
| `src/Signet.Cli` | headless CLI (round-trip checks) |
| `src/Signet.Controls.TreeDataGrid` | fork of the MIT Avalonia TreeDataGrid |
| `tests/` | unit tests, headless view tests, EPUB corpus |

## Icons
From Sigil and calibre, some AI-generated. Later additions come from Google's
[Material Icons](https://github.com/google/material-design-icons) (Apache-2.0) and Microsoft's
[Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT), composed by
`scripts/icons/build_icons.py` (see [`scripts/icons/README.md`](scripts/icons/README.md)).

## License
**GNU GPL v3 or later** ([`LICENSE`](LICENSE)): Signet derives from Sigil (GPLv3) and includes code and icons adapted
from calibre (GPLv3). `src/Signet.Controls.TreeDataGrid` is a fork of the
[Avalonia TreeDataGrid](https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid) (MIT, © .NET Foundation and
Contributors) and keeps the MIT license (`LICENSE.md`, `UPSTREAM.md` there).
