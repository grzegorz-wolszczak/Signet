# Signet

A desktop editor for EPUB 2 and EPUB 3 ebooks, written in C# with [Avalonia](https://avaloniaui.net/)
(vibe coded with AI).

## Project status: use at your own risk

Signet is still under heavy, active development. New features are added all the time (and new
bugs are discovered every day!), so there is **no guarantee that any part of it works correctly**. Everyone who uses it does
so entirely at their own risk. If Signet ruins your book, you have only yourself to blame, so
always keep a backup copy of the original.

## Where this project comes from

Signet is a sandbox experiment: porting an existing C++ application to .NET with the help of AI.
The reference was **[Sigil](https://github.com/Sigil-Ebook/Sigil)** (C++ / Qt6), the open-source
EPUB editor maintained by the Sigil-Ebook project (based on commit
[`654c579`](https://github.com/Sigil-Ebook/Sigil/tree/654c579bb65636ca4fdea1720057a4bc82a71f04)).
I picked Sigil as the base because I think it is a great editor.

Along the way the project picked up features from other sources:

- several features are modelled on the ebook editor in **[calibre](https://github.com/kovidgoyal/calibre)**
  (Edit Book), by Kovid Goyal;
- others are my own additions.

The code is now a hybrid of these approaches and has drifted away from upstream Sigil, although
the UI still looks very similar. This is **not** Sigil and is not affiliated with the Sigil or
calibre projects.

## Features unique to Signet

These features were designed for Signet. As far as I know, neither Sigil nor calibre (nor any other
EPUB editor I know of) has them:

- **Highlighting the clicked element in Preview**: clicking in Code View does more than scroll the
  Preview to the matching place (as Sigil does). It also highlights that element, so it is easy to
  spot on a long page. The style can be set in Preferences → Preview: background or outline, colours
  for light and dark themes, opacity, and whether the highlight fades after a delay.
- **Merge Content**: joins adjacent sibling elements of the same kind into one. One use is
  paragraphs that an import split apart
  (`<p>The cat</p> <p>sat on the mat</p>` → `<p>The cat sat on the mat</p>`).
  The selection only has to start inside the first element and end inside the last one.
- **Cascade-aware CSS class rename**: the class can be renamed from two places:
  - from Code View of an XHTML file, with the caret on a name in `class="…"`. The rename covers
    either the whole book or only the elements with the same nesting in the document structure.
    In the second case the style definitions are copied under the new name.
  - from a stylesheet or a `<style>` block, with the caret on the class in a selector. Only the
    elements that actually use the rules of that stylesheet change. Each source that defines the
    class is handled on its own: renamed in place, copied, or left unchanged.

  Sigil's "Rename Selected Class" and calibre's "Rename the class" change the class everywhere,
  without looking at the cascade.
- **Cleanup (Tools → Cleanup)**: a single dialog with independent **CSS**, **HTML** and **Files**
  tabs. Each tab is planned and applied separately. You see a full preview of every change before
  anything is modified, can tick or untick single items, and can double-click an item to jump to
  it in the code. How it works:
  - **Risk analysis based on the CSS cascade**: every step simulates the cascade for each affected
    element before and after the change. If a change would alter how the book looks, it is
    marked ⚠, left unticked, and lists its consequences. You have to accept it explicitly.
  - **CSS**: rules with the same selector or the same properties are merged. The merged rule goes to
    the first place where the styling stays the same. Selector lists that are risky to merge
    (vendor-prefixed, Selectors 4, unparsable) are flagged. `@import` is followed everywhere.
  - **HTML cleanup**: this is **not** Prettify or Mend, which only fix or reformat the markup. It
    removes structure that adds nothing:
    - nested `<div>`s with identical attributes are collapsed into one;
    - empty `<p>`, `<span>` and `<div>` elements (whitespace only) are removed;
    - bare `<span>`s without attributes are unwrapped.

    Elements with an `id` are never removed.
- **Remove span** (Code View context menu): unwraps the `<span>` at the caret. It can also unwrap
  every identical span (same attributes) in the file or in the whole book. Risky removals go through
  the same ⚠ confirmation as in Cleanup.
- **Live CSS Panel with the element hierarchy**: shows the matching rules of every element from
  `<body>` down to the element at the caret. Inherited properties that a closer element overrides
  are struck through, with the winning rule (selector, file and line) shown next to them. The panel
  sometimes shows more than needed, so this may still change.
- **Notifications panel**: a history of all status bar messages of the session. Warnings about
  blocked or failed operations are highlighted and counted on the bell icon in the status bar.
- **Switching the UI language (Polish / English) on the fly**, without restarting the application.

## Sigil features that are not implemented

Compared with Sigil, the following features are missing from Signet:

- **Python plugin framework**: no Plugins menu, plugin runner or plugin preferences page.
- **Automation lists**: no "Automate" lists or automation editor.
- **Python function replacements in Find & Replace**: replacement patterns of the form
  `\F<name>` and the function editor are not supported. Such a pattern is treated as literal text.
- **Index Editor**: no Index Editor, "Mark for Index", "Add to Index" or "Create Index"
  (index generation).
- **Git-based checkpoints**: there are no Sigil repositories (Manage Repositories, Commit,
  Checkout, Log, checkpoint compare). Signet has its own, simpler checkpoint snapshots instead.
- **Audio, video and PDF viewing**: tabs for these resources show only file information and an
  "open externally" button. There is no playback and no PDF rendering.
- **External editors**: no "Open With" (other application) for resources and no "Edit with
  external XHTML editor" action. The external editor path can be set in Preferences, but nothing
  uses it.
- **Change Reading Order dialog**: the reading order can be changed only with Move Up / Move Down
  and Sort in the Book Browser.
- **Help menu links**: no User Guide, FAQ, Tutorials, website or donation links, and no Markdown
  viewer for plugin help.
- **File dialog with preview**: the "Add Existing Files" dialog is the system dialog, without
  Sigil's built-in file preview.

Signet is built for the repository owner's personal use. There are no plans to turn it into a
product, so there are no releases, support or roadmap commitments.

## Building

Requires the .NET SDK **10.x**.

```bash
dotnet build -c Release
dotnet test  -c Release
dotnet run --project src/Signet.App
```

## Project layout

| Project | Role |
|---|---|
| `src/Signet.Core` | domain model (EPUB/OCF/OPF, resources, import/export), no UI dependencies |
| `src/Signet.App` | Avalonia desktop application (MVVM, CommunityToolkit.Mvvm) |
| `src/Signet.Cli` | headless command-line tool (round-trip checks) |
| `tests/` | unit tests, Avalonia headless view tests and a test EPUB corpus |

## Icons

The icons used in the application come from Sigil and calibre. Some of them were generated with
AI.

## License

**GNU GPL v3 or later**. See [`LICENSE`](LICENSE).

Signet is a derivative work of Sigil (GPLv3) and includes code adapted from calibre (GPLv3) and
some of its icons, so it is distributed under the same license.
