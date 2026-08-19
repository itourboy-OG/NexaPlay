[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'NexaPlay.Owner\NexaPlay.Owner.csproj'
$publishDir = Join-Path $projectRoot 'owner-publish'
$outputDir = Join-Path $projectRoot 'owner-tools'

[xml]$project = Get-Content -LiteralPath $projectFile
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'NexaPlay.Owner.csproj does not contain a Version value.' }

$resolvedRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedPublish = [IO.Path]::GetFullPath($publishDir)
if (-not $resolvedPublish.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to clean an owner publish directory outside the project: $resolvedPublish" }
if (Test-Path -LiteralPath $resolvedPublish) { Remove-Item -LiteralPath $resolvedPublish -Recurse -Force }

dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Owner Studio publish failed with exit code $LASTEXITCODE." }

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$source = Join-Path $publishDir 'NexaPlay.Owner.exe'
$destination = Join-Path $outputDir "NexaPlay-Owner-Studio-v$version.exe"
Copy-Item -LiteralPath $source -Destination $destination -Force
$file = Get-Item -LiteralPath $destination
$hash = Get-FileHash -LiteralPath $destination -Algorithm SHA256
Write-Host 'Private owner tool ready:' -ForegroundColor Green
Write-Host "  $($file.FullName)"
Write-Host "  Size: $([Math]::Round($file.Length / 1MB, 2)) MB"
Write-Host "  SHA-256: $($hash.Hash)"
