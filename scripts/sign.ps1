[CmdletBinding()]
param([Parameter(Mandatory)][string]$CertificatePath, [Parameter(Mandatory)][SecureString]$CertificatePassword, [string]$ArtifactsPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts'))
$ErrorActionPreference = 'Stop'
$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertificatePath, $CertificatePassword)
Get-ChildItem -LiteralPath $ArtifactsPath -Recurse -Include '*.exe','*.dll' | ForEach-Object { Set-AuthenticodeSignature -FilePath $_.FullName -Certificate $certificate -HashAlgorithm SHA256 | Out-Null }
