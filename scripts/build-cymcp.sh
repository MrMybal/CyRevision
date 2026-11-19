#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
python3 "$repo_root/scripts/verify-cymcp-snapshot.py"
case "$(uname -s)" in Linux) platform=linux ;; Darwin) platform=osx ;; *) echo 'Use build-cymcp.ps1 on Windows.' >&2; exit 2 ;; esac
case "$(uname -m)" in x86_64) architecture=x64 ;; aarch64|arm64) architecture=arm64 ;; *) echo 'Unsupported native architecture.' >&2; exit 2 ;; esac
rid="$platform-$architecture"
if [[ "${1:-$rid}" != "$rid" ]]; then echo 'Run the SDK build on its target architecture, or supply a prebuilt CyMcpNativeRuntimeDirectory.' >&2; exit 2; fi
cmake -S "$repo_root/native/CyRevision.CyMcp" -B "$repo_root/artifacts/cymcp-build/$rid" -DCMAKE_BUILD_TYPE=Release
cmake --build "$repo_root/artifacts/cymcp-build/$rid" --config Release --parallel 4
cmake --install "$repo_root/artifacts/cymcp-build/$rid" --config Release --prefix "$repo_root/artifacts/cymcp/$rid" --component CyRevisionCyMcp
