#!/usr/bin/env bash
set -euo pipefail

configuration="${1:-Release}"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_root="${repository_root}/artifacts"

dotnet restore "${repository_root}/CyRevision.sln"
dotnet build "${repository_root}/CyRevision.sln" -c "${configuration}" --no-restore
dotnet test "${repository_root}/CyRevision.sln" -c "${configuration}" --no-build --no-restore
dotnet publish "${repository_root}/src/CyRevision.Desktop/CyRevision.Desktop.csproj" -c "${configuration}" --no-build --no-restore -o "${output_root}/desktop"
dotnet publish "${repository_root}/src/CyRevision.Server/CyRevision.Server.csproj" -c "${configuration}" --no-build --no-restore -o "${output_root}/server"

echo "CyRevision publié dans ${output_root}"
