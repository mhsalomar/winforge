[CmdletBinding()]
param([Parameter(Mandatory)][string]$CertificatePath, [Parameter(Mandatory)][SecureString]$CertificatePassword, [string]$ArtifactsPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'), [string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertificatePath, $CertificatePassword)
$signedFiles = Get-ChildItem -LiteralPath $ArtifactsPath -Recurse -Include '*.exe','*.dll','*.msi'
foreach ($file in $signedFiles) {
  $result = Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $certificate -HashAlgorithm SHA256
  if ($result.Status -notin 'Valid','UnknownError') { throw "Signing failed for $($file.FullName): $($result.StatusMessage)" }
}
foreach ($runtime in 'win-x64','win-arm64') {
  $directory = Join-Path $ArtifactsPath "portable-$runtime"
  if (-not (Test-Path $directory)) { continue }
  $zip = Join-Path $ArtifactsPath "WinForge-$Version-$runtime-portable.zip"
  if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
  Compress-Archive -Path (Join-Path $directory '*') -DestinationPath $zip -CompressionLevel Optimal
}
$files = Get-ChildItem -LiteralPath $ArtifactsPath -File | Where-Object Extension -in '.zip','.msi'
$hashes = $files | ForEach-Object { "{0} *{1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content -LiteralPath (Join-Path $ArtifactsPath 'SHA256SUMS.txt') -Value $hashes -Encoding ascii
