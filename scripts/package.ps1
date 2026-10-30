[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$appPath = Join-Path $repoRoot 'artifacts\app\Click Clock.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw 'Run scripts/build.ps1 before packaging.' }
$fileVersion = [Version](Get-Item -LiteralPath $appPath).VersionInfo.FileVersion
$version = '{0}.{1}.{2}' -f $fileVersion.Major,$fileVersion.Minor,$fileVersion.Build
$releasePath = Join-Path $repoRoot ('artifacts\release\v' + $version)
$stagingPath = Join-Path $repoRoot ('artifacts\package-staging\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $releasePath,$stagingPath -Force | Out-Null

$releaseApp = Join-Path $releasePath 'Click-Clock.exe'
$releaseUsage = Join-Path $releasePath 'Usage.zh-CN.md'
Copy-Item -LiteralPath $appPath -Destination $releaseApp -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\usage.zh-CN.md') -Destination $releaseUsage -Force
Copy-Item -LiteralPath $appPath -Destination (Join-Path $stagingPath 'Click Clock.exe')
Copy-Item -LiteralPath $releaseUsage,(Join-Path $repoRoot 'LICENSE') -Destination $stagingPath
$zipPath = Join-Path $releasePath ('Click-Clock-v' + $version + '-win.zip')
Compress-Archive -LiteralPath @((Join-Path $stagingPath 'Click Clock.exe'),(Join-Path $stagingPath 'Usage.zh-CN.md'),
    (Join-Path $stagingPath 'LICENSE')) -DestinationPath $zipPath -Force

$checksums = foreach ($file in @($releaseApp,$releaseUsage,$zipPath)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $hash + '  ' + [System.IO.Path]::GetFileName($file)
}
[System.IO.File]::WriteAllLines((Join-Path $releasePath 'SHA256SUMS.txt'),[string[]]$checksums,[System.Text.Encoding]::ASCII)
Write-Output "Release files: $releasePath"
