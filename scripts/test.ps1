[CmdletBinding()]
param([switch]$IncludeLayout)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$testPath = Join-Path $repoRoot 'artifacts\tests\ClickClock.Tests.exe'
if (-not (Test-Path -LiteralPath $testPath)) { throw 'Run scripts/build.ps1 before running tests.' }
$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
$resultsPath = Join-Path $repoRoot ('artifacts\test-results\' + $runName)
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null
$arguments = @($resultsPath)
if ($IncludeLayout) { $arguments += '--layout' }
& $testPath @arguments 2>&1 | Tee-Object -FilePath (Join-Path $resultsPath 'test.log')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. See the test.log file in the results directory.' }
Write-Output "Results: $resultsPath"
