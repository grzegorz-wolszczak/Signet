# Icon generator

`build_icons.py` creates the vector icons of the menu commands that had no icon and adds them to the three icon sets of
the application (`src/Signet.App/Assets/Icons/{MainIcons,FluentIcons,MaterialIcons}.axaml`). It also gives the commands
of the spec their icon key in `src/Signet.App/Actions/AppActionCatalog.cs` (with `--catalog`).

```bash
python scripts/icons/build_icons.py --catalog
```

* `icons_spec.py` is the list: icon key → glyphs (and an optional badge) per family, and command id → icon key.
* The generated `DrawingImage`s sit between the `BEGIN generated` / `END generated` comments at the end of each
  dictionary and are replaced on every run. Do not edit them by hand.
* `sources/` holds the SVGs the spec uses (downloaded on first run, then kept, so a build needs no network).

| Family | Used for | Package (pinned in the script) | License |
|---|---|---|---|
| Material Icons, `filled` | the Material set, the shapes of the Default set | `@material-design-icons/svg` 0.14.15 | Apache-2.0 (`sources/LICENSE-material-icons.txt`) |
| Fluent UI System Icons, `regular` 16 px | the Fluent set | `@fluentui/svg-icons` 1.1.344 | MIT (`sources/LICENSE-fluent-ui-system-icons.txt`) |

Both licenses are compatible with Signet's GPL-3.0-or-later. Nothing else is copied from those packages.

How an icon is composed:

* **glyph**: one glyph of the family. The Default set draws the Material glyph in its blue; the other two sets use
  the accent color of the existing sets.
* **badge**: a smaller glyph in the bottom-right corner (the Default set colors it green for add/check, red for
  remove/close/delete, orange otherwise). The base is cut out around the badge so it stays readable in a single color.
* **base `@key`**: the base is an icon that already exists in the set (e.g. "Save As" is the existing Save icon with a
  badge), so related commands look related.
* **multi**: for a command that works on all files, the icon of the single-file command as a bigger copy on top of two
  smaller ones. (The colored PNGs of "Prettify/Mend all HTML files" are made the same way, see
  `src/Signet.App/Assets/Icons/Calibre/README.md`.)
