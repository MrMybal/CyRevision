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
build_agent_project="$repository_root/src/CyRevision.Build.Agent/CyRevision.Build.Agent.csproj"
release_root="$repository_root/artifacts/release/$version"
publish_directory="$release_root/$rid"
bundle_root="$release_root/CyRevision.app"
app_root="$bundle_root/Contents/Resources/app"
dmg_stage="$release_root/dmg-$rid"
dmg_path="$release_root/CyRevision-$version-$rid.dmg"
portable_archive="$release_root/CyRevision-$version-$rid-portable.zip"
checksum_file="$release_root/SHA256SUMS-$rid.txt"
iconset="$release_root/cyrevision.iconset"
main_executable="$bundle_root/Contents/MacOS/CyRevision.Desktop"
packaged_executable="$app_root/CyRevision.Desktop"
launcher_source="$repository_root/installer/macos/CyRevisionLauncher.c"

rm -rf -- "$publish_directory" "$bundle_root" "$dmg_stage" "$iconset"
mkdir -p "$publish_directory" "$bundle_root/Contents/MacOS" "$app_root" "$iconset"

bash "$repository_root/scripts/prepare-syncthing-runtime.sh" "$rid"

dotnet restore "$solution"
dotnet restore "$desktop_project" --runtime "$rid"
dotnet restore "$agent_project" --runtime "$rid"
dotnet restore "$build_agent_project" --runtime "$rid"
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
dotnet publish "$build_agent_project" \
  -c Release \
  --runtime "$rid" \
  --self-contained true \
  --no-restore \
  -o "$publish_directory/BuildAgent" \
  "/p:Version=$version" \
  /p:DebugType=None \
  /p:DebugSymbols=false

cp -a "$publish_directory/." "$app_root/"
chmod +x "$packaged_executable"
chmod +x "$app_root/Agent/CyRevision.Discord.Agent"
chmod +x "$app_root/BuildAgent/CyRevision.Build.Agent"
cp "$repository_root/LICENSE" "$bundle_root/Contents/Resources/"
cp "$repository_root/README.md" "$bundle_root/Contents/Resources/"

# Keep Contents/MacOS limited to the real CFBundleExecutable. Managed DLLs and
# application resources belong under Contents/Resources; recent codesign
# versions reject unsigned .NET assemblies when they are placed in MacOS.
case "$rid" in
  osx-x64) launcher_arch="x86_64" ;;
  osx-arm64) launcher_arch="arm64" ;;
esac
xcrun --sdk macosx clang \
  -arch "$launcher_arch" \
  -mmacosx-version-min=11.0 \
  -Os \
  -Wall \
  -Wextra \
  -Werror \
  "$launcher_source" \
  -o "$main_executable"
chmod +x "$main_executable"
xcrun lipo -verify_arch "$launcher_arch" "$main_executable"
xcrun lipo -verify_arch "$launcher_arch" "$packaged_executable"

unexpected_macos_entry="$(find "$bundle_root/Contents/MacOS" -mindepth 1 -maxdepth 1 ! -name 'CyRevision.Desktop' -print -quit)"
if [[ -n "$unexpected_macos_entry" ]]; then
  echo "Unexpected file in Contents/MacOS: $unexpected_macos_entry" >&2
  exit 1
fi

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

# Ad-hoc signing keeps the bundle internally consistent. Sign every packaged
# Mach-O first, then let codesign sign the native launcher and app envelope.
# --deep cannot be used because runtime plugin folders are not macOS bundles.
if [[ ! -x "$main_executable" || ! -x "$packaged_executable" ]]; then
  echo "The macOS launcher or packaged application executable is missing." >&2
  exit 1
fi
while IFS= read -r -d '' candidate; do
  if file -b "$candidate" | grep -q 'Mach-O'; then
    codesign --force --sign - "$candidate"
  fi
done < <(find "$app_root" -type f -print0)

# This command signs both the main executable and the application envelope.
# A Developer ID identity and notarization can later replace this ad-hoc identity.
codesign --force --sign - "$bundle_root"

while IFS= read -r -d '' candidate; do
  if file -b "$candidate" | grep -q 'Mach-O'; then
    codesign --verify --strict --verbose=2 "$candidate"
  fi
done < <(find "$app_root" -type f -print0)
codesign --verify --strict --verbose=2 "$bundle_root"

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
