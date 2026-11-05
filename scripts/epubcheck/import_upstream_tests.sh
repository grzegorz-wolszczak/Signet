#!/usr/bin/env bash
# Copies the conformance suite of EPUBCheck (its Cucumber scenarios and the fixtures they check) into
# tests/corpus/epubcheck.
#
# Usage: import_upstream_tests.sh <epubcheck-checkout> [<tag-or-commit>]
#
# <epubcheck-checkout> is a checkout of the tag named in src/Signet.EpubCheck.Core/UPSTREAM.md. The script replaces
# tests/corpus/epubcheck/ (the fixtures are not edited by hand: they keep the faults they were written to show), keeps the
# license beside them and writes a README that names where they come from.
set -euo pipefail

checkout="${1:?usage: import_upstream_tests.sh <epubcheck-checkout> [<tag-or-commit>]}"
label="${2:-$(git -C "$checkout" describe --tags --always)}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
target="$root/tests/corpus/epubcheck"

rm -rf "$target"
mkdir -p "$target"
cp -R --no-preserve=mode,ownership "$checkout/src/test/resources/." "$target/"
cp --no-preserve=mode,ownership "$checkout/LICENSE.md" "$target/LICENSE-EPUBCheck.md"

# The fixtures must stay byte for byte as they are (CRLF, BOM, broken encodings are part of what they test).
printf '# Byte for byte: the fixtures of EPUBCheck are checked as they are, never converted.\n* -text\n' > "$target/.gitattributes"

cat > "$target/README.md" <<README
# tests/corpus/epubcheck

The conformance suite of **EPUBCheck** ($label): the Cucumber scenarios (\`*.feature\`) and the fixtures they check
(unpacked and packed publications, single documents). It is copied from \`src/test/resources\` of
<https://github.com/w3c/epubcheck> by \`scripts/epubcheck/import_upstream_tests.sh\`; do not edit it by hand — a fixture
tidied up for the tests stops being evidence. The license is in \`LICENSE-EPUBCheck.md\` (BSD-3-Clause).

\`tests/Signet.EpubCheck.Conformance.Tests\` runs the scenarios against the port (see \`EPUBCHECK_PLAN.md\`).
README

echo "Imported $(find "$target" -type f | wc -l) files into $target"
