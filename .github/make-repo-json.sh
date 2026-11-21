#!/usr/bin/env bash
# Turns one built plugin manifest into its entry in the listing Dalamud reads from this custom plugin repository.
#   make-repo-json.sh <built Plugin.json> <owner/repo> <release tag> <zip asset name> [changelog]
# Download links point at "the latest release", so the one listing address always serves the newest versions.
# release.yml joins the entries of every plugin into repo.json.
set -euo pipefail
manifest=$1
base="https://github.com/$2/releases/latest/download"

jq --arg base "$base" --arg zip "$4" --arg repo "https://github.com/$2" --arg notes "${5:-}" --argjson now "$(date +%s)" '
  . + {
    RepoUrl: $repo,
    DownloadLinkInstall: ($base + "/" + $zip),
    DownloadLinkUpdate: ($base + "/" + $zip),
    DownloadLinkTesting: ($base + "/" + $zip),
    TestingAssemblyVersion: .AssemblyVersion,
    IsHide: false,
    IsTestingExclusive: false,
    DownloadCount: 0,
    LastUpdate: $now
  }
  + (if $notes != "" then {Changelog: $notes} else {} end)
' "$manifest"
