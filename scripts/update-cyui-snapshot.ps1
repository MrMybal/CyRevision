#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$SourceRepository,
    [string]$Revision = 'ff023e646c6a3038b3dfbfd53f6d2b4a2f5955e3'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$vendorRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'native/vendor/CyUI'))
$sourceRoot = (Resolve-Path -LiteralPath $SourceRepository).ProviderPath
$commit = (& git -C $sourceRoot rev-parse "$Revision^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $commit -ne $Revision) { throw 'An exact CyUI commit is required.' }
$paths = @('CMakeLists.txt', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'include', 'src', 'third_party', 'docs', 'data', 'locales')
$staging = Join-Path $repositoryRoot ('artifacts/cyui-snapshot-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$archive = Join-Path $staging 'source.tar'
& git -C $sourceRoot archive --format=tar "--output=$archive" $commit -- @paths
if ($LASTEXITCODE -ne 0) { throw 'CyUI archive failed.' }
$export = Join-Path $staging 'export'
New-Item -ItemType Directory -Path $export -Force | Out-Null
& tar -xf $archive -C $export
if ($LASTEXITCODE -ne 0) { throw 'CyUI extraction failed.' }
# This directory contains only the dependency snapshot, never application files.
if ($vendorRoot -ne [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'native/vendor/CyUI'))) { throw 'Unexpected vendor target.' }
if (Test-Path -LiteralPath $vendorRoot) {
    foreach ($oldFile in Get-ChildItem -LiteralPath $vendorRoot -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($vendorRoot, $oldFile.FullName)
        if (-not (Test-Path -LiteralPath (Join-Path $export $relative))) { Remove-Item -LiteralPath $oldFile.FullName }
    }
}
New-Item -ItemType Directory -Path $vendorRoot -Force | Out-Null
Copy-Item -Path (Join-Path $export '*') -Destination $vendorRoot -Recurse -Force
$files = [ordered]@{}
Get-ChildItem -LiteralPath $vendorRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($vendorRoot, $_.FullName).Replace('\', '/')
    $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$lock = [ordered]@{
    repository = 'https://github.com/MrMybal/CyUI'
    commit = $commit
    tree = (& git -C $sourceRoot rev-parse "$commit^{tree}").Trim()
    archiveSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    paths = $paths
    files = $files
}
$lock | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repositoryRoot 'native/cyui.lock.json') -Encoding utf8NoBOM
Write-Output "CyUI snapshot: $commit ($($files.Count) files)"
