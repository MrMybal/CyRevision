param(
    [string]$CyUiPath = '',
    [switch]$SkipManagedBuild,
    [string]$OutputDirectory = 'artifacts/native-v2'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CyUiPath)) {
    $CyUiPath = Join-Path $repositoryRoot 'native/vendor/CyUI'
}
$CyUiPath = (Resolve-Path -LiteralPath $CyUiPath).ProviderPath
$dependencyDirectory = Join-Path $repositoryRoot '.tools/native-json'
New-Item -ItemType Directory -Path $dependencyDirectory -Force | Out-Null
$jsonHeader = Join-Path $dependencyDirectory 'json.hpp'
$expectedHash = 'aaf127c04cb31c406e5b04a63f1ae89369fccde6d8fa7cdda1ed4f32dfc5de63'
if (-not (Test-Path -LiteralPath $jsonHeader)) {
    Invoke-WebRequest 'https://raw.githubusercontent.com/nlohmann/json/v3.12.0/single_include/nlohmann/json.hpp' -OutFile $jsonHeader
}
if ((Get-FileHash -LiteralPath $jsonHeader -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
    throw 'Native JSON dependency checksum mismatch.'
}
$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
if ($cmakeCommand) { $cmake = $cmakeCommand.Source }
else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $cmake = Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
}
if (-not (Test-Path -LiteralPath $cmake)) { throw 'CMake with Visual Studio C++ tools is required.' }
$nativeBuild = Join-Path $repositoryRoot 'artifacts/native-v2-build'
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
& $cmake -S (Join-Path $repositoryRoot 'native/CyRevision.Native') -B $nativeBuild -A x64 "-DCYUI_ROOT=$CyUiPath" "-DJSON_HEADER=$jsonHeader"
if ($LASTEXITCODE -ne 0) { throw 'Native configuration failed.' }
& $cmake --build $nativeBuild --config Release --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Native compilation failed.' }
$ctest = Join-Path (Split-Path -Parent $cmake) 'ctest.exe'
& $ctest --test-dir $nativeBuild -C Release --output-on-failure --no-tests=error --timeout 45
if ($LASTEXITCODE -ne 0) { throw 'Native UI regression tests failed.' }
if (-not $SkipManagedBuild) {
    dotnet publish (Join-Path $repositoryRoot 'src/CyRevision.Desktop/CyRevision.Desktop.csproj') -c Release -o $output --nologo /p:DebugType=None /p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Managed engine compilation failed.' }
}
& $cmake --install $nativeBuild --config Release --prefix $output
if ($LASTEXITCODE -ne 0) { throw 'Native installation failed.' }
Write-Output (Join-Path $output 'CyRevision.Native.exe')
