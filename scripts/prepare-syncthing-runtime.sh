#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <linux-x64|linux-arm64|osx-x64|osx-arm64> [syncthing-version]" >&2
  exit 2
fi

rid="$1"
syncthing_version="${2:-2.1.3}"
case "$rid" in
  linux-x64) platform="linux"; architecture="amd64"; extension="tar.gz" ;;
  linux-arm64) platform="linux"; architecture="arm64"; extension="tar.gz" ;;
  osx-x64) platform="macos"; architecture="amd64"; extension="zip" ;;
  osx-arm64) platform="macos"; architecture="arm64"; extension="zip" ;;
  *) echo "Unsupported Syncthing runtime: $rid" >&2; exit 2 ;;
esac

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_root="$repository_root/src/CyRevision.Desktop/SyncthingRuntime/$rid"
temporary_root="$(mktemp -d)"
asset_name="syncthing-${platform}-${architecture}-v${syncthing_version}.${extension}"
archive_path="$temporary_root/$asset_name"
download_url="https://github.com/syncthing/syncthing/releases/download/v${syncthing_version}/${asset_name}"
release_api_url="https://api.github.com/repos/syncthing/syncthing/releases/tags/v${syncthing_version}"
authorization_header=()
if [[ -n "${GH_TOKEN:-}" ]]; then
  authorization_header=(-H "Authorization: Bearer ${GH_TOKEN}")
fi

cleanup() { rm -rf -- "$temporary_root"; }
trap cleanup EXIT

release_json="$temporary_root/release.json"
curl --fail --location --silent --show-error \
  -H 'User-Agent: CyRevision-release-builder' \
  "${authorization_header[@]}" \
  "$release_api_url" --output "$release_json"
expected_digest="$(jq -r --arg name "$asset_name" '.assets[] | select(.name == $name) | .digest // empty' "$release_json")"
if [[ "$expected_digest" != sha256:* ]]; then
  echo "The official release metadata has no SHA-256 digest for $asset_name." >&2
  exit 1
fi
curl --fail --location --silent --show-error \
  -H 'User-Agent: CyRevision-release-builder' \
  "${authorization_header[@]}" \
  "$download_url" --output "$archive_path"
expected_digest="${expected_digest#sha256:}"
if command -v sha256sum >/dev/null 2>&1; then
  actual_digest="$(sha256sum "$archive_path" | awk '{print $1}')"
else
  actual_digest="$(shasum -a 256 "$archive_path" | awk '{print $1}')"
fi
if [[ "$actual_digest" != "$expected_digest" ]]; then
  echo 'The downloaded Syncthing archive does not match the official GitHub SHA-256 digest.' >&2
  exit 1
fi
mkdir -p "$temporary_root/expanded"
if [[ "$extension" == "zip" ]]; then
  unzip -q "$archive_path" -d "$temporary_root/expanded"
else
  tar -xzf "$archive_path" -C "$temporary_root/expanded"
fi

executable="$(find "$temporary_root/expanded" -type f -name syncthing -print -quit)"
license="$(find "$temporary_root/expanded" -type f -name 'LICENSE*' -print -quit)"
if [[ -z "$executable" || -z "$license" ]]; then
  echo "The verified Syncthing archive is incomplete." >&2
  exit 1
fi

rm -rf -- "$runtime_root"
mkdir -p "$runtime_root"
install -m 0755 "$executable" "$runtime_root/syncthing"
install -m 0644 "$license" "$runtime_root/LICENSE-SYNCTHING.txt"
printf '%s\n' \
  "Syncthing v${syncthing_version}" \
  'Source: https://github.com/syncthing/syncthing' \
  "Release source: https://github.com/syncthing/syncthing/releases/tag/v${syncthing_version}" \
  'License: MPL-2.0' \
  'The downloaded release archive was verified against the SHA-256 digest in the official GitHub release metadata.' \
  > "$runtime_root/SOURCE.txt"
