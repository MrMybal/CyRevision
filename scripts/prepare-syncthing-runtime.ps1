param(
    [string]$Runtime = 'win-x64',
    [string]$SyncthingVersion = '2.1.3'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Runtime -ne 'win-x64')
{
    throw "Unsupported PowerShell Syncthing runtime: $Runtime"
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtimeRoot = Join-Path $repositoryRoot "src\CyRevision.Desktop\SyncthingRuntime\$Runtime"
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "cyrevision-syncthing-$([Guid]::NewGuid().ToString('N'))"
$assetName = "syncthing-windows-amd64-v$SyncthingVersion.zip"
$archivePath = Join-Path $temporaryRoot $assetName
$releaseApiUrl = "https://api.github.com/repos/syncthing/syncthing/releases/tags/v$SyncthingVersion"
$headers = @{ 'User-Agent' = 'CyRevision-release-builder' }
if ($env:GH_TOKEN)
{
    $headers.Authorization = "Bearer $($env:GH_TOKEN)"
}

try
{
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    $release = Invoke-RestMethod -Uri $releaseApiUrl -Headers $headers
    $asset = $release.assets | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
    if (-not $asset -or -not $asset.digest -or -not $asset.digest.StartsWith('sha256:'))
    {
        throw "The official release metadata has no SHA-256 digest for $assetName."
    }
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers $headers -OutFile $archivePath
    $expectedDigest = $asset.digest.Substring('sha256:'.Length)
    $actualDigest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualDigest -ne $expectedDigest.ToLowerInvariant())
    {
        throw 'The downloaded Syncthing archive does not match the official GitHub SHA-256 digest.'
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath (Join-Path $temporaryRoot 'expanded') -Force
    $executable = Get-ChildItem -LiteralPath (Join-Path $temporaryRoot 'expanded') -Filter syncthing.exe -File -Recurse |
        Select-Object -First 1
    $license = Get-ChildItem -LiteralPath (Join-Path $temporaryRoot 'expanded') -Filter 'LICENSE*' -File -Recurse |
        Select-Object -First 1
    if (-not $executable -or -not $license)
    {
        throw 'The verified Syncthing archive does not contain the expected executable and license.'
    }

    if (Test-Path -LiteralPath $runtimeRoot)
    {
        Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
    Copy-Item -LiteralPath $executable.FullName -Destination (Join-Path $runtimeRoot 'syncthing.exe')
    Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $runtimeRoot 'LICENSE-SYNCTHING.txt')
    @(
        "Syncthing v$SyncthingVersion",
        'Source: https://github.com/syncthing/syncthing',
        "Release source: https://github.com/syncthing/syncthing/releases/tag/v$SyncthingVersion",
        'License: MPL-2.0',
        'The downloaded release archive was verified against the SHA-256 digest in the official GitHub release metadata.'
    ) | Set-Content -LiteralPath (Join-Path $runtimeRoot 'SOURCE.txt') -Encoding UTF8
}
finally
{
    if (Test-Path -LiteralPath $temporaryRoot)
    {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
