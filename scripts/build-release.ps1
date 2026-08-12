param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-NativeCommand
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0)
    {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$desktopProject = Join-Path $repositoryRoot 'src\CyRevision.Desktop\CyRevision.Desktop.csproj'
$agentProject = Join-Path $repositoryRoot 'src\CyRevision.Discord.Agent\CyRevision.Discord.Agent.csproj'
$buildAgentProject = Join-Path $repositoryRoot 'src\CyRevision.Build.Agent\CyRevision.Build.Agent.csproj'
$solution = Join-Path $repositoryRoot 'CyRevision.sln'
$installerScript = Join-Path $repositoryRoot 'installer\windows\CyRevision.iss'
$releaseRoot = Join-Path $repositoryRoot "artifacts\release\$Version"
$publishDirectory = Join-Path $releaseRoot 'win-x64'
$agentPublishDirectory = Join-Path $publishDirectory 'Agent'
$buildAgentPublishDirectory = Join-Path $publishDirectory 'BuildAgent'
$portableArchive = Join-Path $releaseRoot "CyRevision-$Version-win-x64-portable.zip"

if (Test-Path -LiteralPath $releaseRoot)
{
    $resolvedReleaseRoot = [IO.Path]::GetFullPath($releaseRoot)
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\release'))
    if (-not $resolvedReleaseRoot.StartsWith($expectedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))
    {
        throw "The release output path is outside the expected artifacts directory: $resolvedReleaseRoot"
    }

    Remove-Item -LiteralPath $resolvedReleaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Invoke-NativeCommand 'dotnet' @('restore', $solution)
Invoke-NativeCommand 'dotnet' @('restore', $desktopProject, '--runtime', 'win-x64')
Invoke-NativeCommand 'dotnet' @('restore', $agentProject, '--runtime', 'win-x64')
Invoke-NativeCommand 'dotnet' @('restore', $buildAgentProject, '--runtime', 'win-x64')
Invoke-NativeCommand 'dotnet' @('build', $solution, '-c', $Configuration, '--no-restore', "/p:Version=$Version")
if (-not $SkipTests)
{
    Invoke-NativeCommand 'dotnet' @('test', $solution, '-c', $Configuration, '--no-build', '--no-restore', "/p:Version=$Version")
}

Invoke-NativeCommand 'dotnet' @(
    'publish', $desktopProject,
    '-c', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-o', $publishDirectory,
    "/p:Version=$Version",
    '/p:DebugType=None',
    '/p:DebugSymbols=false'
)

Invoke-NativeCommand 'dotnet' @(
    'publish', $agentProject,
    '-c', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-o', $agentPublishDirectory,
    "/p:Version=$Version",
    '/p:DebugType=None',
    '/p:DebugSymbols=false'
)

Invoke-NativeCommand 'dotnet' @(
    'publish', $buildAgentProject,
    '-c', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-o', $buildAgentPublishDirectory,
    "/p:Version=$Version",
    '/p:DebugType=None',
    '/p:DebugSymbols=false'
)

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $publishDirectory

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $portableArchive -CompressionLevel Optimal

$innoCompilerCandidates = @(
    (Join-Path $repositoryRoot '.tools\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$innoCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($innoCommand)
{
    $innoCompilerCandidates = @($innoCommand.Source) + $innoCompilerCandidates
}

$innoCompiler = $innoCompilerCandidates |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
    Select-Object -First 1
if (-not $innoCompiler)
{
    throw 'Inno Setup 6 was not found. Install it with: choco install innosetup -y'
}

Invoke-NativeCommand $innoCompiler @(
    "/DAppVersion=$Version",
    "/DSourceDir=$publishDirectory",
    "/DOutputDir=$releaseRoot",
    $installerScript
)

$releaseFiles = Get-ChildItem -LiteralPath $releaseRoot -File |
    Where-Object { $_.Extension -in '.exe', '.zip' } |
    Sort-Object Name
$checksumPath = Join-Path $releaseRoot 'SHA256SUMS-win-x64.txt'
$checksumLines = foreach ($file in $releaseFiles)
{
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding ascii

Write-Host ''
Write-Host "CyRevision $Version release artifacts:" -ForegroundColor Cyan
$releaseFiles + (Get-Item -LiteralPath $checksumPath) |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
Write-Host "Output: $releaseRoot" -ForegroundColor Green
