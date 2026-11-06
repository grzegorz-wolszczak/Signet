#!/usr/bin/env bash
# Copies the schemas of EPUBCheck (RELAX NG, NVDL, Schematron, the DTDs, the catalog, with their licenses) and the two
# XSLT 1.0 stylesheets its build runs on the "*-30.sch" files into src/Signet.EpubCheck.Core/Xml/Schemas.
#
# Usage: import_upstream_schemas.sh <epubcheck-checkout> [<tag-or-commit>]
#
# <epubcheck-checkout> is a checkout of the tag named in src/Signet.EpubCheck.Core/UPSTREAM.md. The files are copied as
# they are: upstream's build preprocesses the "*-30.sch" files (pom.xml, xml-maven-plugin), the port does the same when
# it loads them (Xml/SchemaResources.cs), so nothing here is edited by hand.
set -euo pipefail

checkout="${1:?usage: import_upstream_schemas.sh <epubcheck-checkout> [<tag-or-commit>]}"
label="${2:-$(git -C "$checkout" describe --tags --always)}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
target="$root/src/Signet.EpubCheck.Core/Xml/Schemas"

rm -rf "$target"
mkdir -p "$target/schema" "$target/util/sch"
cp -R --no-preserve=mode,ownership "$checkout/src/main/resources/com/adobe/epubcheck/schema/." "$target/schema/"
cp --no-preserve=mode,ownership "$checkout/src/main/util/sch/"*.xsl "$target/util/sch/"

# Byte for byte, on every platform: the schemas are upstream's files.
printf '# Byte for byte: the schemas of EPUBCheck are kept as upstream ships them (LF).\n* text eol=lf\n' > "$target/.gitattributes"

cat > "$target/README.md" <<README
# Xml/Schemas

The schemas of **EPUBCheck** ($label), copied by \`scripts/epubcheck/import_upstream_schemas.sh\`; do not edit them by
hand.

- \`schema/\` is upstream's \`src/main/resources/com/adobe/epubcheck/schema\`: RELAX NG (\`.rnc\`, \`.rng\`), NVDL,
  Schematron, the DTDs and \`30/epub30-catalog.xml\`. Their licenses are beside them: \`schema/30/LICENSE\` (IDPF, MIT),
  \`schema/30/mod/html5/LICENSE\` (the Nu HTML Checker schemas, MIT), \`schema/30/mod/mathml/LICENSE\` and
  \`schema/30/mod/security/LICENSE\` (W3C Software Notice and License); the rest is EPUBCheck's (BSD-3-Clause,
  \`../../LICENSE-EPUBCheck.md\`).
- \`util/sch/\` is upstream's \`src/main/util/sch\`: the XSLT 1.0 stylesheets upstream's build runs on the
  \`schema/30/*-30.sch\` files (\`iso-sch-include.xsl\`, then \`iso-sch-abstract.xsl\`). The port runs them when it loads
  such a file (\`SchemaResources\`).

They are embedded in \`Signet.EpubCheck.Core\` under their path here (\`schema/30/package-30.rnc\`, …).
README

echo "Imported $(find "$target" -type f | wc -l) files into $target"
