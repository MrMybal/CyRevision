param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))

dotnet restore (Join-Path $repositoryRoot 'CyRevision.sln')
dotnet build (Join-Path $repositoryRoot 'CyRevision.sln') -c $Configuration --no-restore
dotnet test (Join-Path $repositoryRoot 'CyRevision.sln') -c $Configuration --no-build --no-restore
dotnet publish (Join-Path $repositoryRoot 'src/CyRevision.Desktop/CyRevision.Desktop.csproj') -c $Configuration --no-build --no-restore -o (Join-Path $outputRoot 'desktop')
dotnet publish (Join-Path $repositoryRoot 'src/CyRevision.Server/CyRevision.Server.csproj') -c $Configuration --no-build --no-restore -o (Join-Path $outputRoot 'server')

Write-Host "CyRevision publié dans $outputRoot"
