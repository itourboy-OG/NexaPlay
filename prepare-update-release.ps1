[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^https://')] [string]$InstallerUrl
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'NexaPlay\NexaPlay.csproj')
$version = [string]$project.Project.PropertyGroup.Version
$installer = Join-Path $projectRoot "installer\output\NexaPlay-Setup-v$version.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw "Build the v$version player installer first: $installer" }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
$manifest = [ordered]@{
    version = $version
    installerUrl = $InstallerUrl
    sha256 = $hash
    notes = 'Latest NexaPlay improvements and fixes from SauceBoyz.'
    publishedUtc = [DateTime]::UtcNow.ToString('o')
}
$releaseFolder = Join-Path $projectRoot 'release'
New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null
$path = Join-Path $releaseFolder 'update-manifest.json'
$manifest | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding utf8NoBOM
Write-Host "Update manifest ready: $path" -ForegroundColor Green
Write-Host "Installer SHA-256: $hash"
