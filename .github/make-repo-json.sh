#!/usr/bin/env bash
# Turns the manifest the build produced into the listing Dalamud reads from a custom plugin repository.
#   make-repo-json.sh <built ApolloNCC.json> <owner/repo> [changelog]
# The download links point at "the latest release", so this one listing address always serves the newest version.
set -euo pipefail
manifest=$1
base="https://github.com/$2/releases/latest/download"

jq --arg base "$base" --arg repo "https://github.com/$2" --arg notes "${3:-}" --argjson now "$(date +%s)" '[
  . + {
    RepoUrl: $repo,
    IconUrl: ($base + "/icon.png"),
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
