<#
.SYNOPSIS
  Builds Coatrack with the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.

.EXAMPLE
  .\build.ps1          # builds build\Coatrack.exe
  .\build.ps1 -Test    # also builds and runs the unit tests
#>
param([switch]$Test)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'build'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "Can't find csc.exe from .NET Framework 4.x." }

New-Item -ItemType Directory -Force $out | Out-Null

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
Compile ($common + @('/target:winexe', "/out:$exe", "/win32icon:$ico", "/win32manifest:$(Join-Path $root 'src\app.manifest')") + (Sources 'src'))
Write-Host "Built $exe"

# 3. Tests (compiled together with the app's source, using the test runner's entry point).
if ($Test) {
  $testExe = Join-Path $out 'CoatrackTests.exe'
  Compile ($common + @('/target:exe', "/out:$testExe", '/main:Coatrack.Tests.TestRunner') + (Sources 'src') + (Sources 'tests'))
  & $testExe
  if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
