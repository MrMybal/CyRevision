param([string]$Source = '../CyMCPSDK')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourcePath = if ([IO.Path]::IsPathRooted($Source)) { $Source } else { Join-Path $root $Source }
$sourcePath = (Resolve-Path -LiteralPath $sourcePath).ProviderPath
$revision = (& git -C $sourcePath rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $revision -ne '791d4d7d7680213b282ec8529d248a10f8494d77') { throw 'Unexpected CyMCPSDK revision.' }
if (& git -C $sourcePath status --porcelain --untracked-files=no) { throw 'Preserve local SDK edits before snapshotting.' }
$target = Join-Path $root 'native/vendor/CyMCPSDK'
if (Test-Path -LiteralPath $target) { throw 'The SDK snapshot already exists; do not overwrite it.' }
$archive = Join-Path $root 'artifacts/cymcp-source.zip'
New-Item -ItemType Directory -Path (Split-Path -Parent $archive) -Force | Out-Null
& git -C $sourcePath archive --format=zip --output $archive HEAD CMakeLists.txt include src cmake third_party LICENSE THIRD_PARTY_NOTICES.md README.md VERSION.md
if ($LASTEXITCODE -ne 0) { throw 'SDK snapshot failed.' }
Expand-Archive -LiteralPath $archive -DestinationPath $target
$hashes = [ordered]@{}
Get-ChildItem -LiteralPath $target -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($target, $_.FullName).Replace('\', '/')
    $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{version='0.7.0';revision=$revision;repository='https://github.com/MrMybal/CyMCPSDK';modules=@('Core','Platform','Http','MCP','AI');files=$hashes}
[IO.File]::WriteAllText((Join-Path $root 'native/cymcp.lock.json'), ($manifest | ConvertTo-Json -Depth 6) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "CyMCPSDK snapshot: $revision"
