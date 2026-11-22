#!/usr/bin/env bash
# One-time setup of the GitHub repository for the CI/release flow (see "CI and releases" in AGENTS.md):
#   - merge settings: squash only, the squash commit = the pull request's title and description (the release pipeline
#     reads the version keywords from it), head branches deleted after the merge;
#   - the ruleset of main (.github/rulesets/main.json): no direct pushes, no force pushes or deletion, changes only
#     through a pull request whose "Linux tests" check passed; repository admins may bypass it.
# Run it again after changing main.json: it updates the existing ruleset.
#
#   scripts/release/setup-github.sh [owner/repo]
#
# Needs the GitHub CLI (gh), logged in as an admin of the repository.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
repo="${1:-grzegorz-wolszczak/Signet}"

gh api -X PATCH "repos/$repo" \
    -F allow_squash_merge=true -F allow_merge_commit=false -F allow_rebase_merge=false \
    -f squash_merge_commit_title=PR_TITLE -f squash_merge_commit_message=PR_BODY \
    -F delete_branch_on_merge=true >/dev/null
echo "Merge settings updated."

id="$(gh api "repos/$repo/rulesets" --jq '.[] | select(.name == "main") | .id')"
if [ -n "$id" ]; then
    gh api -X PUT "repos/$repo/rulesets/$id" --input "$root/.github/rulesets/main.json" >/dev/null
    echo "Ruleset main ($id) updated."
else
    gh api -X POST "repos/$repo/rulesets" --input "$root/.github/rulesets/main.json" >/dev/null
    echo "Ruleset main created."
fi
