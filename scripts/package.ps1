[CmdletBinding()]
param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts "portable-$Runtime"
if (-not ([IO.Path]::GetFullPath($publish)).StartsWith(([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe publish path.' }
if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
New-Item -ItemType Directory -Path $publish -Force | Out-Null
dotnet publish (Join-Path $root 'src/WinForge.UI/WinForge.UI.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $publish
$worker = Join-Path $artifacts "worker-$Runtime"
if (-not ([IO.Path]::GetFullPath($worker)).StartsWith(([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe worker path.' }
if (Test-Path $worker) { Remove-Item -LiteralPath $worker -Recurse -Force }
dotnet publish (Join-Path $root 'src/WinForge.Worker/WinForge.Worker.csproj') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $worker
Get-ChildItem -LiteralPath $worker -Filter 'WinForge.Worker.*' | Copy-Item -Destination $publish -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $publish
Copy-Item -LiteralPath (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $publish
$zip = Join-Path $artifacts "WinForge-$Version-$Runtime-portable.zip"
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
$hashes = Get-ChildItem -LiteralPath $artifacts -Filter '*.zip' | ForEach-Object { "{0} *{1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Value $hashes -Encoding ascii
Write-Output $zip
