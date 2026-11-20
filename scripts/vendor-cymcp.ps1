param([string]$Source = '../CyMCPSDK', [string]$Revision = '34d4bcae126bc83d1a2925b5047dac36e3d09d92', [switch]$Update)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Revision -notmatch '^[0-9a-f]{40}$') { throw 'Use a full exact CyMCPSDK commit.' }
$sourcePath = if ([IO.Path]::IsPathRooted($Source)) { $Source } else { Join-Path $root $Source }
if (-not (Test-Path -LiteralPath $sourcePath)) {
    $sourcePath = Join-Path $root 'artifacts/cymcp-source'
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        & git clone 'https://github.com/MrMybal/CyMCPSDK' $sourcePath
        if ($LASTEXITCODE -ne 0) { throw 'Cannot access CyMCPSDK sources.' }
    }
}
$sourcePath = (Resolve-Path -LiteralPath $sourcePath).ProviderPath
$resolvedRevision = & git -C $sourcePath rev-parse "$Revision`^{commit}"
if ($LASTEXITCODE -ne 0 -or $resolvedRevision.Trim() -ne $Revision) { throw 'Use an exact available CyMCPSDK commit.' }
if (& git -C $sourcePath status --porcelain --untracked-files=no) { throw 'Preserve local SDK edits before snapshotting.' }
$target = Join-Path $root 'native/vendor/CyMCPSDK'
if (Test-Path -LiteralPath $target) {
    if (-not $Update) { throw 'The SDK snapshot already exists; use -Update after reviewing the new revision.' }
    $oldLock = Get-Content -LiteralPath (Join-Path $root 'native/cymcp.lock.json') -Raw | ConvertFrom-Json
    $oldFiles = @($oldLock.files.PSObject.Properties.Name)
    foreach ($file in Get-ChildItem -LiteralPath $target -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($target, $file.FullName).Replace('\', '/')
        if ($relative -notin $oldFiles -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $oldLock.files.$relative) {
            throw 'Preserve local vendored SDK changes before updating.'
        }
    }
}
$archive = Join-Path $root 'artifacts/cymcp-source.zip'
New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
& git -C $sourcePath archive --format=zip --output $archive $Revision CMakeLists.txt include src cmake third_party LICENSE THIRD_PARTY_NOTICES.md README.md VERSION.md
if ($LASTEXITCODE -ne 0) { throw 'SDK snapshot failed.' }
Expand-Archive -LiteralPath $archive -DestinationPath $target -Force
$hashes = [ordered]@{}
Get-ChildItem -LiteralPath $target -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($target, $_.FullName).Replace('\', '/')
    $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{version='0.7.1';revision=$Revision;repository='https://github.com/MrMybal/CyMCPSDK';modules=@('Core','Platform','Http','MCP','AI');files=$hashes}
[IO.File]::WriteAllText((Join-Path $root 'native/cymcp.lock.json'), ($manifest | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "CyMCPSDK snapshot: $Revision"
