# tests/corpus/epubcheck

The conformance suite of **EPUBCheck** (5.4.1-SNAPSHOT@6000e8e): the Cucumber scenarios (`*.feature`) and the fixtures they check
(unpacked and packed publications, single documents). It is copied from `src/test/resources` of
<https://github.com/w3c/epubcheck> by `scripts/epubcheck/import_upstream_tests.sh`; do not edit it by hand — a fixture
tidied up for the tests stops being evidence. The license is in `LICENSE-EPUBCheck.md` (BSD-3-Clause).

`tests/Signet.EpubCheck.Conformance.Tests` runs the scenarios against the port (see `EPUBCHECK_PLAN.md`).
