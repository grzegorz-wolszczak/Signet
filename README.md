# Signet

A desktop editor for EPUB 2 and EPUB 3 ebooks, written in C# with [Avalonia](https://avaloniaui.net/).

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

## License

**GNU GPL v3 or later**. See [`LICENSE`](LICENSE).

Signet is a derivative work of Sigil (GPLv3) and includes code adapted from calibre (GPLv3) and
some of its icons, so it is distributed under the same license.
