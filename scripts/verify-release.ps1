[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$sums = Join-Path $artifacts 'SHA256SUMS.txt'
if (-not (Test-Path $sums)) { throw 'SHA256SUMS.txt is missing.' }
foreach ($line in Get-Content -LiteralPath $sums) {
  if ($line -notmatch '^([0-9a-f]{64}) \*(.+)$') { throw "Invalid checksum line: $line" }
  $file = Join-Path $artifacts $Matches[2]
  if (-not (Test-Path $file)) { throw "Missing artifact: $file" }
  if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]) { throw "Checksum mismatch: $file" }
}
Write-Output 'Release artifacts verified.'
