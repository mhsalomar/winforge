[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $root 'WinForge.sln') -c $Configuration --no-restore --logger 'console;verbosity=normal'
