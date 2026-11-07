#!/usr/bin/env bash
# Publishes the standalone EPUBCheck tool (epubcheck.net) for win-x64 and linux-x64 and packs each build into an archive.
#
#   scripts/epubcheck/publish.sh [--rid win-x64|linux-x64] [--smoke] [--output <folder>]
#
# Without --rid both are published. The result is <output>/<rid>/ (the executable, the licenses and the README) and
# <output>/epubcheck.net-<version>-<rid>.zip (win-x64) or .tar.gz (linux-x64). The default output folder is artifacts/epubcheck.
# --smoke runs the published executable of the host's platform on a corpus book and fails when it does not report a clean book.
# Needs the .NET SDK and, to publish a RID for the first time, access to NuGet (the runtime pack of the RID).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$root/src/Signet.EpubCheck.Cli/Signet.EpubCheck.Cli.csproj"
output="$root/artifacts/epubcheck"
rids=(win-x64 linux-x64)
smoke=0

while [ $# -gt 0 ]; do
    case "$1" in
        --rid) rids=("$2"); shift 2 ;;
        --smoke) smoke=1; shift ;;
        --output) output="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

pack() {
    # pack <folder> <archive>: a .zip for Windows, a .tar.gz otherwise.
    local folder="$1" archive="$2"
    rm -f "$archive"
    case "$archive" in
        *.zip)
            if command -v zip >/dev/null 2>&1; then
                (cd "$folder" && zip -q -r "$archive" .)
            else
                (cd "$folder" && python3 -m zipfile -c "$archive" . 2>/dev/null || python -m zipfile -c "$archive" .)
            fi ;;
        *) tar -C "$folder" -czf "$archive" . ;;
    esac
}

host_rid() {
    case "$(uname -s)" in
        MINGW*|MSYS*|CYGWIN*) echo win-x64 ;;
        Linux) echo linux-x64 ;;
        *) echo unsupported ;;
    esac
}

# One version for every platform: the prefix of the port and one build time.
prefix="$(grep -o '<VersionPrefix>[^<]*' "$root/version/EpubCheckVersion.props" | head -n 1 | sed 's/<VersionPrefix>//')"
stamp="$(date -u +%Y%m%d%H%M)"
version="$prefix-$stamp"

mkdir -p "$output"
for rid in "${rids[@]}"; do
    case "$rid" in
        win-x64|linux-x64) ;;
        *) echo "Unsupported RID: $rid (win-x64 and linux-x64 only)" >&2; exit 2 ;;
    esac

    folder="$output/$rid"
    rm -rf "$folder"
    echo "Publishing $rid ..."
    dotnet publish "$project" -c Release -r "$rid" --self-contained true -p:SignetBuildTimestamp="$stamp" -o "$folder" -nologo -v:q

    # The symbols of the referenced libraries are copied beside the executable; they do not belong in the archive.
    rm -f "$folder"/*.pdb "$folder"/*.xml
    cp "$root/LICENSE" "$folder/LICENSE"
    cp "$root/src/Signet.EpubCheck.Core/LICENSE-EPUBCheck.md" "$folder/LICENSE-EPUBCheck.md"
    cp "$root/src/Signet.EpubCheck.Cli/README.md" "$folder/README.md"

    exe="epubcheck.net"
    [ "$rid" = "win-x64" ] && exe="epubcheck.net.exe"
    if [ "$rid" = "$(host_rid)" ] && ! "$folder/$exe" --version >/dev/null 2>&1; then
        echo "The published executable does not run: $folder/$exe" >&2
        exit 1
    fi

    extension="tar.gz"
    [ "$rid" = "win-x64" ] && extension="zip"
    archive="$output/epubcheck.net-$version-$rid.$extension"
    pack "$folder" "$archive"
    echo "  $archive"
done

if [ "$smoke" = 1 ]; then
    rid="$(host_rid)"
    if [ "$rid" = unsupported ]; then
        echo "No published executable for this platform." >&2
        exit 1
    fi

    exe="$output/$rid/epubcheck.net"
    [ "$rid" = "win-x64" ] && exe="$output/$rid/epubcheck.net.exe"
    book="$root/tests/corpus/epubcheck/reporting/files/minimal.epub"
    echo "Smoke test: $exe on minimal.epub"
    result="$("$exe" "$book" --locale en 2>&1)" || { echo "$result"; echo "Smoke test failed: exit code $?" >&2; exit 1; }
    echo "$result" | grep -q "No errors or warnings detected" || { echo "$result"; echo "Smoke test failed: the book was not reported clean." >&2; exit 1; }
    echo "Smoke test passed."
fi
