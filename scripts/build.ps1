[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts' }
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $repoRoot $OutputDirectory }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The Windows .NET Framework C# compiler was not found.' }

$appOutput = Join-Path $OutputDirectory 'app'
$testOutput = Join-Path $OutputDirectory 'tests'
New-Item -ItemType Directory -Path $appOutput,$testOutput -Force | Out-Null
$sourceRoot = Join-Path $repoRoot 'src\ClickClock'
$appPath = Join-Path $appOutput 'Click Clock.exe'
$testPath = Join-Path $testOutput 'ClickClock.Tests.exe'
$common = @('/nologo','/optimize+','/platform:anycpu','/codepage:65001',
    '/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Xml.dll')
$sources = @((Join-Path $sourceRoot 'Core.cs'),(Join-Path $sourceRoot 'App.cs'),(Join-Path $sourceRoot 'Celebration.cs'))
$manifest = '/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')
$icon = '/win32icon:' + (Join-Path $repoRoot 'assets\app.ico')

& $compilerPath @common /target:winexe "/out:$appPath" $manifest $icon @sources (Join-Path $sourceRoot 'AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
& $compilerPath @common /target:exe /main:DualClock.Tests "/out:$testPath" $manifest @sources (Join-Path $repoRoot 'tests\ClickClock.Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Write-Output "Built: $appPath"
Write-Output "Built: $testPath"
