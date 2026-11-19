param([string]$Configuration='Release',[string]$RuntimeIdentifier='win-x64')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if ($RuntimeIdentifier -ne 'win-x64') { throw 'Build this native SDK on its target platform; Windows supports win-x64.' }
$lock=Get-Content -LiteralPath (Join-Path $root 'native/cymcp.lock.json') -Raw | ConvertFrom-Json
foreach($entry in $lock.files.PSObject.Properties) {
    $file=Join-Path $root ('native/vendor/CyMCPSDK/'+$entry.Name)
    if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) { throw "SDK snapshot mismatch: $($entry.Name)" }
}
$cmakeCommand=Get-Command cmake -ErrorAction SilentlyContinue
$cmake=if($cmakeCommand){$cmakeCommand.Source}else{$null}
if (-not $cmake) {
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $cmake=Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
}
if (-not (Test-Path -LiteralPath $cmake)) { throw 'CMake and Visual Studio C++ tools are required for CyMCPSDK.' }
$build=Join-Path $root 'artifacts/cymcp-build/win-x64'
$destination=Join-Path $root 'artifacts/cymcp/win-x64'
& $cmake -S (Join-Path $root 'native/CyRevision.CyMcp') -B $build -A x64
if ($LASTEXITCODE -ne 0) { throw 'CyMCPSDK configuration failed.' }
& $cmake --build $build --config $Configuration --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'CyMCPSDK compilation failed.' }
& $cmake --install $build --config $Configuration --prefix $destination --component CyRevisionCyMcp
if ($LASTEXITCODE -ne 0) { throw 'CyMCPSDK installation failed.' }
Write-Output (Join-Path $destination 'CyRevision.CyMcpNative.dll')
