<#
.SYNOPSIS
  Builds Coatrack with the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.

.EXAMPLE
  .\build.ps1                            # builds build\Coatrack.exe (version 0.0.0-dev)
  .\build.ps1 -Test                      # also builds and runs the unit tests
  .\build.ps1 -Version 1.2.0 -Package    # stamps the version and writes dist\Coatrack-1.2.0.zip
#>
param(
  [switch]$Test,
  # Semantic version, with or without a leading "v" (a release tag such as v1.2.0 works as is).
  [string]$Version = '0.0.0-dev',
  # Also write a zip and SHA-256 checksums to dist\.
  [switch]$Package
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'build'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "Can't find csc.exe from .NET Framework 4.x." }

# Version: "v1.2.3-beta.1" -> informational "1.2.3-beta.1", numeric "1.2.3.0".
$Version = $Version.TrimStart('v', 'V')
if ($Version -notmatch '^(\d+)(\.\d+){0,3}(-[0-9A-Za-z.-]+)?$') { throw "Version '$Version' isn't like 1.2.3 or 1.2.3-beta.1." }
$numeric = ($Version -split '-')[0].Split('.')
while ($numeric.Count -lt 4) { $numeric += '0' }
$numeric = $numeric -join '.'

New-Item -ItemType Directory -Force $out | Out-Null
$versionFile = Join-Path $out 'VersionInfo.cs'
Set-Content -Encoding UTF8 $versionFile @"
[assembly: System.Reflection.AssemblyVersion("$numeric")]
[assembly: System.Reflection.AssemblyFileVersion("$numeric")]
[assembly: System.Reflection.AssemblyInformationalVersion("$Version")]
"@

$refs = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Management.dll',
          'System.Runtime.Serialization.dll', 'System.Xml.dll', 'Microsoft.VisualBasic.dll') | ForEach-Object { "/r:$_" }
$common = @('/nologo', '/optimize+', '/nowarn:1607', '/langversion:5', '/codepage:65001') + $refs

function Sources([string[]]$dirs) {
  $dirs | ForEach-Object { Get-ChildItem (Join-Path $root $_) -Recurse -Filter *.cs } | ForEach-Object { $_.FullName }
}
function Compile([string[]]$csArgs) {
  & $csc @csArgs
  if ($LASTEXITCODE -ne 0) { throw "Compilation failed." }
}

# 1. A small tool draws the app icon, so there are no binary assets in the repo.
$ico = Join-Path $out 'Coatrack.ico'
Compile ($common + @('/target:exe', "/out:$(Join-Path $out 'IconTool.exe')") + (Sources 'tools') + (Join-Path $root 'src\UI\HatIcon.cs'))
& (Join-Path $out 'IconTool.exe') $ico | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'IconTool failed.' }

# 2. The app.
$exe = Join-Path $out 'Coatrack.exe'
Compile ($common + @('/target:winexe', "/out:$exe", "/win32icon:$ico", "/win32manifest:$(Join-Path $root 'src\app.manifest')", $versionFile) + (Sources 'src'))
Write-Host "Built $exe ($Version)"

# 3. Tests (compiled together with the app's source, using the test runner's entry point).
if ($Test) {
  $testExe = Join-Path $out 'CoatrackTests.exe'
  Compile ($common + @('/target:exe', "/out:$testExe", '/main:Coatrack.Tests.TestRunner', $versionFile) + (Sources 'src') + (Sources 'tests'))
  & $testExe
  if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

# 4. Release files.
if ($Package) {
  $dist = Join-Path $root 'dist'
  if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
  $staging = Join-Path $dist "Coatrack-$Version"
  New-Item -ItemType Directory -Force $staging | Out-Null
  Copy-Item $exe, (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $staging
  $zip = Join-Path $dist "Coatrack-$Version.zip"
  Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
  Copy-Item $exe (Join-Path $dist 'Coatrack.exe')
  Remove-Item $staging -Recurse -Force
  $sums = Get-ChildItem $dist -File | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
  Set-Content -Encoding ASCII (Join-Path $dist 'SHA256SUMS.txt') $sums
  Write-Host "Packaged:"; Get-ChildItem $dist | ForEach-Object { "  dist\$($_.Name)" }
}
