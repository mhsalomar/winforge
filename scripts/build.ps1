[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet restore (Join-Path $root 'WinForge.sln')
dotnet format (Join-Path $root 'WinForge.sln') --verify-no-changes --no-restore
dotnet build (Join-Path $root 'WinForge.sln') -c $Configuration --no-restore
