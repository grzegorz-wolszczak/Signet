#!/usr/bin/env bash
# Prints the version of the next Signet release: MAJOR.MINOR.PATCH.BUILD.
#
#   scripts/release/next-version.sh <file with the commit messages of the push>
#
# The last version is the highest git tag vX.Y.Z.W (the tags must have been fetched). Without any tag the first release
# is 0.1.0.0. The commit messages decide which number goes up (whole words, any letter case):
#   "minor"            -> X.(Y+1).0.0
#   "patch", "bugfix"  -> X.Y.(Z+1).0
#   none of them       -> X.Y.Z.(W+1)
# "minor" wins over "patch"/"bugfix". MAJOR is never bumped by a message: tag a commit vN.0.0.0 by hand, the next
# release counts from there.
set -euo pipefail

messages="${1:?usage: next-version.sh <messages file>}"

last="$(git tag --list 'v*' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' | sed 's/^v//' | sort -V | tail -n 1 || true)"
if [ -z "$last" ]; then
    echo "0.1.0.0"
    exit 0
fi

IFS=. read -r major minor patch build <<< "$last"

if grep -qiwE 'minor' "$messages"; then
    minor=$((minor + 1)); patch=0; build=0
elif grep -qiwE 'patch|bugfix' "$messages"; then
    patch=$((patch + 1)); build=0
else
    build=$((build + 1))
fi

echo "$major.$minor.$patch.$build"
