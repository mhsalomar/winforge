[CmdletBinding()]
param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts/portable-win-x64'
if (-not (Test-Path (Join-Path $publish 'WinForge.UI.exe'))) { & (Join-Path $PSScriptRoot 'package.ps1') -Runtime win-x64 -Version $Version }
$source = Join-Path $root 'artifacts/WinForge.generated.wxs'
$output = Join-Path $root "artifacts/WinForge-$Version-win-x64.msi"
$namespace = 'http://wixtoolset.org/schemas/v4/wxs'
$document = [Xml.XmlDocument]::new()
$wix = $document.CreateElement('Wix', $namespace); $document.AppendChild($wix) | Out-Null
$package = $document.CreateElement('Package', $namespace); $wix.AppendChild($package) | Out-Null
$package.SetAttribute('Name', 'WinForge'); $package.SetAttribute('Manufacturer', 'WinForge contributors'); $package.SetAttribute('Version', $Version); $package.SetAttribute('UpgradeCode', '7D813C21-2EC5-4D55-92BC-A6DC5BE30D17'); $package.SetAttribute('Scope', 'perMachine')
$upgrade = $document.CreateElement('MajorUpgrade', $namespace); $upgrade.SetAttribute('DowngradeErrorMessage', 'A newer version of WinForge is already installed.'); $package.AppendChild($upgrade) | Out-Null
$media = $document.CreateElement('MediaTemplate', $namespace); $media.SetAttribute('EmbedCab', 'yes'); $media.SetAttribute('CompressionLevel', 'high'); $package.AppendChild($media) | Out-Null
$standard = $document.CreateElement('StandardDirectory', $namespace); $standard.SetAttribute('Id', 'ProgramFiles64Folder'); $package.AppendChild($standard) | Out-Null
$install = $document.CreateElement('Directory', $namespace); $install.SetAttribute('Id', 'INSTALLFOLDER'); $install.SetAttribute('Name', 'WinForge'); $standard.AppendChild($install) | Out-Null
$feature = $document.CreateElement('Feature', $namespace); $feature.SetAttribute('Id', 'Complete'); $feature.SetAttribute('Title', 'WinForge'); $feature.SetAttribute('Level', '1'); $package.AppendChild($feature) | Out-Null
$directoryNodes = @{ '' = $install }
function Get-StableId([string]$prefix, [string]$value) {
  $bytes = [Text.Encoding]::UTF8.GetBytes($value.ToLowerInvariant())
  $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).Substring(0, 24)
  return "$prefix$hash"
}
foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName) {
  $relative = [IO.Path]::GetRelativePath($publish, $file.FullName)
  $relativeDirectory = [IO.Path]::GetDirectoryName($relative)
  $parent = $install
  $pathSoFar = ''
  if ($relativeDirectory) {
    foreach ($part in $relativeDirectory.Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)) {
      $pathSoFar = if ($pathSoFar) { Join-Path $pathSoFar $part } else { $part }
      if (-not $directoryNodes.ContainsKey($pathSoFar)) {
        $directory = $document.CreateElement('Directory', $namespace); $directory.SetAttribute('Id', (Get-StableId 'D' $pathSoFar)); $directory.SetAttribute('Name', $part); $parent.AppendChild($directory) | Out-Null; $directoryNodes[$pathSoFar] = $directory
      }
      $parent = $directoryNodes[$pathSoFar]
    }
  }
  $componentId = Get-StableId 'C' $relative
  $component = $document.CreateElement('Component', $namespace); $component.SetAttribute('Id', $componentId); $component.SetAttribute('Guid', '*'); $parent.AppendChild($component) | Out-Null
  $fileNode = $document.CreateElement('File', $namespace); $fileNode.SetAttribute('Id', (Get-StableId 'F' $relative)); $fileNode.SetAttribute('Source', $file.FullName); $fileNode.SetAttribute('KeyPath', 'yes'); $component.AppendChild($fileNode) | Out-Null
  $reference = $document.CreateElement('ComponentRef', $namespace); $reference.SetAttribute('Id', $componentId); $feature.AppendChild($reference) | Out-Null
}
$settings = [Xml.XmlWriterSettings]::new(); $settings.Indent = $true; $settings.Encoding = [Text.UTF8Encoding]::new($false)
$writer = [Xml.XmlWriter]::Create($source, $settings); $document.Save($writer); $writer.Dispose()
dotnet tool restore
dotnet wix build $source -arch x64 -o $output
if ($LASTEXITCODE -ne 0) { throw "WiX build failed with exit code $LASTEXITCODE." }
$files = Get-ChildItem -LiteralPath (Join-Path $root 'artifacts') -File | Where-Object Extension -in '.zip','.msi'
$hashes = $files | ForEach-Object { "{0} *{1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content -LiteralPath (Join-Path $root 'artifacts/SHA256SUMS.txt') -Value $hashes -Encoding ascii
Write-Output $output
