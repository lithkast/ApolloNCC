#!/usr/bin/env bash
# Turns the manifest the build produced into the listing Dalamud reads from a custom plugin repository.
#   make-repo-json.sh <built ApolloNCC.json> <owner/repo> <release tag> [changelog]
# The download links point at "the latest release", so this one listing address always serves the newest version.
# The icon comes straight from the repository at the release's tag: a release download is sent as a redirect to a
# file of unknown type, which Dalamud's plugin installer does not show.
set -euo pipefail
manifest=$1
base="https://github.com/$2/releases/latest/download"

icon="https://raw.githubusercontent.com/$2/$3/ApolloNCC/images/icon.png"

jq --arg base "$base" --arg icon "$icon" --arg repo "https://github.com/$2" --arg notes "${4:-}" --argjson now "$(date +%s)" '[
  . + {
    RepoUrl: $repo,
    IconUrl: $icon,
    DownloadLinkInstall: ($base + "/latest.zip"),
    DownloadLinkUpdate: ($base + "/latest.zip"),
    DownloadLinkTesting: ($base + "/latest.zip"),
    TestingAssemblyVersion: .AssemblyVersion,
    IsHide: false,
    IsTestingExclusive: false,
    DownloadCount: 0,
    LastUpdate: $now
  }
  + (if $notes != "" then {Changelog: $notes} else {} end)
]' "$manifest"
