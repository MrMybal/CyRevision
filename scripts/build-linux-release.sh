#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "Usage: $0 <version> [linux-x64|linux-arm64]" >&2
  exit 2
fi

version="$1"
rid="${2:-linux-x64}"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid release version: $version" >&2
  exit 2
fi

case "$rid" in
  linux-x64) deb_architecture="amd64" ;;
  linux-arm64) deb_architecture="arm64" ;;
  *) echo "Unsupported Linux runtime: $rid" >&2; exit 2 ;;
esac

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
solution="$repository_root/CyRevision.sln"
desktop_project="$repository_root/src/CyRevision.Desktop/CyRevision.Desktop.csproj"
agent_project="$repository_root/src/CyRevision.Discord.Agent/CyRevision.Discord.Agent.csproj"
build_agent_project="$repository_root/src/CyRevision.Build.Agent/CyRevision.Build.Agent.csproj"
release_root="$repository_root/artifacts/release/$version"
publish_directory="$release_root/$rid"
package_root="$release_root/package-$rid"
portable_archive="$release_root/CyRevision-$version-$rid-portable.tar.gz"
deb_package="$release_root/CyRevision-$version-$rid.deb"
checksum_file="$release_root/SHA256SUMS-$rid.txt"

rm -rf -- "$publish_directory" "$package_root"
mkdir -p "$publish_directory"

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

cp "$repository_root/LICENSE" "$publish_directory/"
cp "$repository_root/README.md" "$publish_directory/"
chmod +x "$publish_directory/CyRevision.Desktop"
chmod +x "$publish_directory/Agent/CyRevision.Discord.Agent"
chmod +x "$publish_directory/BuildAgent/CyRevision.Build.Agent"
tar -C "$publish_directory" -czf "$portable_archive" .

install -d \
  "$package_root/DEBIAN" \
  "$package_root/usr/bin" \
  "$package_root/usr/lib/cyrevision" \
  "$package_root/usr/share/applications" \
  "$package_root/usr/share/doc/cyrevision" \
  "$package_root/usr/share/icons/hicolor/512x512/apps"
install -d "$package_root/usr/lib/systemd/user"
cp -a "$publish_directory/." "$package_root/usr/lib/cyrevision/"
ln -s ../lib/cyrevision/CyRevision.Desktop "$package_root/usr/bin/cyrevision"
ln -s ../lib/cyrevision/Agent/CyRevision.Discord.Agent "$package_root/usr/bin/cyrevision-discord-agent"
ln -s ../lib/cyrevision/BuildAgent/CyRevision.Build.Agent "$package_root/usr/bin/cyrevision-build-agent"
install -m 0644 "$repository_root/installer/linux/cyrevision-discord-agent.service" \
  "$package_root/usr/lib/systemd/user/cyrevision-discord-agent.service"
install -m 0644 "$repository_root/installer/linux/cyrevision-build-agent.service" \
  "$package_root/usr/lib/systemd/user/cyrevision-build-agent.service"
install -m 0644 "$repository_root/installer/linux/cyrevision.desktop" \
  "$package_root/usr/share/applications/cyrevision.desktop"
install -m 0644 "$repository_root/src/CyRevision.Desktop/Assets/Branding/cyrevision-icon-512.png" \
  "$package_root/usr/share/icons/hicolor/512x512/apps/cyrevision.png"
install -m 0644 "$repository_root/LICENSE" "$package_root/usr/share/doc/cyrevision/copyright"

installed_size="$(du -sk "$package_root/usr" | cut -f1)"
sed \
  -e "s/@VERSION@/$version/g" \
  -e "s/@ARCHITECTURE@/$deb_architecture/g" \
  -e "s/@INSTALLED_SIZE@/$installed_size/g" \
  "$repository_root/installer/linux/control.in" > "$package_root/DEBIAN/control"

dpkg-deb --build --root-owner-group "$package_root" "$deb_package"
(
  cd "$release_root"
  sha256sum "$(basename "$portable_archive")" "$(basename "$deb_package")" > "$(basename "$checksum_file")"
)

echo "CyRevision $version Linux release artifacts:"
ls -lh "$deb_package" "$portable_archive" "$checksum_file"
