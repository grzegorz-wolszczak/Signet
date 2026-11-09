# Xml/Schemas

The schemas of **EPUBCheck** (5.4.1-SNAPSHOT@6000e8e), copied by `scripts/epubcheck/import_upstream_schemas.sh`; do not edit them by
hand.

- `schema/` is upstream's `src/main/resources/com/adobe/epubcheck/schema`: RELAX NG (`.rnc`, `.rng`), NVDL,
  Schematron, the DTDs and `30/epub30-catalog.xml`. Their licenses are beside them: `schema/30/LICENSE` (IDPF, MIT),
  `schema/30/mod/html5/LICENSE` (the Nu HTML Checker schemas, MIT), `schema/30/mod/mathml/LICENSE` and
  `schema/30/mod/security/LICENSE` (W3C Software Notice and License); the rest is EPUBCheck's (BSD-3-Clause,
  `../../LICENSE-EPUBCheck.md`).
- `util/sch/` is upstream's `src/main/util/sch`: the XSLT 1.0 stylesheets upstream's build runs on the
  `schema/30/*-30.sch` files (`iso-sch-include.xsl`, then `iso-sch-abstract.xsl`). The port runs them when it loads
  such a file (`SchemaResources`).

They are embedded in `Signet.EpubCheck.Core` under their path here (`schema/30/package-30.rnc`, …).
