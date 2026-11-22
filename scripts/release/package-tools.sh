#!/usr/bin/env bash
# Packages the command-line tools of a Signet release for linux-x64 and win-x64:
#   signet-cli-<version>-<rid>.tar.gz|.zip   Signet.Cli, the version of the release
#   epubcheck.net-<port version>-<rid>.*     the standalone EPUBCheck port (scripts/epubcheck/publish.sh), its own version
#
#   scripts/release/package-tools.sh <version> [<output folder>]
#
# Both are self-contained single files. Runs on Linux (win-x64 is cross-published); needs the .NET SDK and zip.
# The default output folder is artifacts/release.
set -euo pipefail

version="${1:?usage: package-tools.sh <version> [<output folder>]}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output="${2:-$root/artifacts/release}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
work="$root/artifacts/tools-work"
rm -rf "$work"
mkdir -p "$work"

for rid in linux-x64 win-x64; do
    folder="$work/signet-cli-$rid"
    echo "== Publishing Signet.Cli $version ($rid)"
    dotnet publish "$root/src/Signet.Cli/Signet.Cli.csproj" -c Release -r "$rid" --self-contained true \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
        -p:PublishReadyToRun=true -p:SignetReleaseVersion="$version" -p:DebugType=none \
        -p:PublishDocumentationFiles=false -o "$folder" -nologo
    rm -f "$folder"/*.pdb "$folder"/*.xml
    cp "$root/LICENSE" "$folder/LICENSE"

    if [ "$rid" = linux-x64 ]; then
        "$folder/signet" --version
        tar -C "$folder" -czf "$output/signet-cli-$version-$rid.tar.gz" .
    else
        rm -f "$output/signet-cli-$version-$rid.zip"
        (cd "$folder" && zip -q -r "$output/signet-cli-$version-$rid.zip" .)
    fi
done

echo "== Publishing epubcheck.net"
"$root/scripts/epubcheck/publish.sh" --smoke --output "$work/epubcheck"
cp "$work"/epubcheck/epubcheck.net-*.zip "$work"/epubcheck/epubcheck.net-*.tar.gz "$output/"

ls -la "$output"
