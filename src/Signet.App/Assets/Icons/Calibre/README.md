# Icons from calibre

The files in this directory are unmodified copies from the **calibre** project
(<https://github.com/kovidgoyal/calibre>, `resources/images/`, commit
`59bd2bc260bf0601429cb12a8731f91e92aa47d7`).

| File | Icon key | Signet actions |
|---|---|---|
| `beautify.png` | `beautify` | Beautify code (current file / all HTML files) |
| `html-fix.png` | `html-fix` | Fix code (current file / all HTML files) |

Copyright (C) 2008-2026 Kovid Goyal &lt;kovid@kovidgoyal.net&gt;, licensed under the GNU GPL v3
(the `Files: *` section of calibre's `COPYRIGHT` file), which is compatible with Signet's license (GPL-3.0-or-later).

The icons are colored and are loaded as a `Bitmap` by `IconThemeManager` under all built-in
icon sets (Default / Fluent / Material). The "Custom" set can override them by providing
its own SVG path under the same key in `icons.json`.
