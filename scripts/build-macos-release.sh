#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <version> <osx-x64|osx-arm64>" >&2
  exit 2
fi

version="$1"
rid="$2"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid release version: $version" >&2
  exit 2
fi
if [[ "$rid" != "osx-x64" && "$rid" != "osx-arm64" ]]; then
  echo "Unsupported macOS runtime: $rid" >&2
  exit 2
fi

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
solution="$repository_root/CyRevision.sln"
desktop_project="$repository_root/src/CyRevision.Desktop/CyRevision.Desktop.csproj"
agent_project="$repository_root/src/CyRevision.Discord.Agent/CyRevision.Discord.Agent.csproj"
release_root="$repository_root/artifacts/release/$version"
publish_directory="$release_root/$rid"
bundle_root="$release_root/CyRevision.app"
dmg_stage="$release_root/dmg-$rid"
dmg_path="$release_root/CyRevision-$version-$rid.dmg"
portable_archive="$release_root/CyRevision-$version-$rid-portable.zip"
checksum_file="$release_root/SHA256SUMS-$rid.txt"
iconset="$release_root/cyrevision.iconset"

rm -rf -- "$publish_directory" "$bundle_root" "$dmg_stage" "$iconset"
mkdir -p "$publish_directory" "$bundle_root/Contents/MacOS" "$bundle_root/Contents/Resources" "$iconset"

dotnet restore "$solution"
dotnet restore "$desktop_project" --runtime "$rid"
dotnet restore "$agent_project" --runtime "$rid"
dotnet build "$solution" -c Release --no-restore "/p:Version=$version"
if [[ "${CYREVISION_SKIP_TESTS:-0}" != "1" ]]; then
  dotnet test "$solution" -c Release --no-build --no-restore "/p:Version=$version"
fi
dotnet publish "$desktop_project" \
  -c Release \
  --runtime "$rid" \
  --self-contained true \
  --no-restore \
  -o "$publish_directory" \
  "/p:Version=$version" \
  /p:DebugType=None \
  /p:DebugSymbols=false
dotnet publish "$agent_project" \
  -c Release \
  --runtime "$rid" \
  --self-contained true \
  --no-restore \
  -o "$publish_directory/Agent" \
  "/p:Version=$version" \
  /p:DebugType=None \
  /p:DebugSymbols=false

cp -a "$publish_directory/." "$bundle_root/Contents/MacOS/"
chmod +x "$bundle_root/Contents/MacOS/CyRevision.Desktop"
chmod +x "$bundle_root/Contents/MacOS/Agent/CyRevision.Discord.Agent"
cp "$repository_root/LICENSE" "$bundle_root/Contents/Resources/"
cp "$repository_root/README.md" "$bundle_root/Contents/Resources/"

master_icon="$repository_root/src/CyRevision.Desktop/Assets/Branding/cyrevision-icon-master.png"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$master_icon" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double_size=$((size * 2))
  sips -z "$double_size" "$double_size" "$master_icon" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$bundle_root/Contents/Resources/cyrevision.icns"

bundle_version="$(printf '%s' "$version" | sed -E 's/[^0-9.].*$//')"
sed \
  -e "s/@VERSION@/$version/g" \
  -e "s/@BUNDLE_VERSION@/$bundle_version/g" \
  "$repository_root/installer/macos/Info.plist.in" > "$bundle_root/Contents/Info.plist"
plutil -lint "$bundle_root/Contents/Info.plist"

# Ad-hoc signing keeps the bundle internally consistent. A Developer ID signature
# and notarization can be enabled later through protected CI secrets.
codesign --force --deep --sign - "$bundle_root"
codesign --verify --deep --strict "$bundle_root"

ditto -c -k --sequesterRsrc --keepParent "$bundle_root" "$portable_archive"
mkdir -p "$dmg_stage"
cp -a "$bundle_root" "$dmg_stage/"
ln -s /Applications "$dmg_stage/Applications"
hdiutil create \
  -volname "CyRevision $version" \
  -srcfolder "$dmg_stage" \
  -ov \
  -format UDZO \
  "$dmg_path"

(
  cd "$release_root"
  shasum -a 256 "$(basename "$dmg_path")" "$(basename "$portable_archive")" > "$(basename "$checksum_file")"
)

echo "CyRevision $version macOS release artifacts:"
ls -lh "$dmg_path" "$portable_archive" "$checksum_file"
