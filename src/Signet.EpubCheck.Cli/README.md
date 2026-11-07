# epubcheck.net

A standalone EPUB checker: a C# port of the W3C's [EPUBCheck](https://github.com/w3c/epubcheck) 5.4.0. It checks EPUB 2 and
EPUB 3 publications (a packaged `.epub`, an expanded folder, or a single document) and reports the same message ids with the
same severities as the Java tool. It needs no Java and no installed .NET: the executable is self-contained.

## Run

```
epubcheck.net book.epub
epubcheck.net --mode exp path/to/expanded-book --save
epubcheck.net --mode xhtml chapter.xhtml
epubcheck.net book.epub --json report.json
epubcheck.net --help
```

The options are the ones of EPUBCheck 5.4.0 (`--help` lists them): `--mode opf|xhtml|svg|nav|mo|exp`, `-v 2|3` (the EPUB
version of a single document; a whole EPUB is checked against the version it declares), `--profile default|dict|edupub|idx|preview`, the reporting levels `-f -e -w -i -u`, `--quiet`,
`--failonwarnings`, `--out|-o`, `--json|-j`, `--xmp|-x` (a file name, or `-` for the console), `--maxOfEachMessage`,
`--customMessages`, `--listChecks` and `--locale`. The exit code is 0 when there is no error (and, with `--failonwarnings`, no
warning), 1 otherwise.

What differs from the Java tool:

- The messages are in **English** and **Polish** (`--locale pl`); the other locales of the Java tool are not ported.
- Checking a remote file (`http://`, `https://`) is not supported; the tool never touches the network.
- `--save` keeps the EPUB made from an expanded folder as `<folder>.epub`, as the Java tool does; a file of that name that
  already exists is never overwritten or deleted when `--save` is not given.

The list of deliberate differences is in `src/Signet.EpubCheck.Core/UPSTREAM.md` of the source repository.

## Requirements

- **Windows x64** (`win-x64`): nothing to install.
- **Linux x64** (`linux-x64`): the usual runtime libraries of .NET (see
  <https://learn.microsoft.com/dotnet/core/install/linux>) and ICU (`libicu`), which the Polish texts need.

## Licenses

The port is distributed under the GNU GPL v3 or later (`LICENSE`). What comes from EPUBCheck (message ids and texts, schemas, the
structure of the checks) is © Adobe Systems Incorporated, IDPF and W3C under the BSD 3-Clause license (`LICENSE-EPUBCheck.md`).

## Build

From the source repository: `scripts/epubcheck/publish.sh` (both platforms) or `scripts/epubcheck/publish.sh --rid win-x64 --smoke`.
