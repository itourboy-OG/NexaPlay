[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'NexaPlay\NexaPlay.csproj'
$publishDir = Join-Path $projectRoot 'publish'
$installerDir = Join-Path $projectRoot 'installer'
$innoCompiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

[xml]$project = Get-Content -LiteralPath $projectFile
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'NexaPlay.csproj does not contain a Version value.'
}
if (-not (Test-Path -LiteralPath $innoCompiler)) {
    throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php and run this script again.'
}

$resolvedRoot = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$resolvedPublish = [System.IO.Path]::GetFullPath($publishDir)
if (-not $resolvedPublish.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside the project: $resolvedPublish"
}
if (Test-Path -LiteralPath $resolvedPublish) {
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}

Write-Host "Publishing NexaPlay v$version..."
dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Write-Host 'Compiling the Windows installer...'
& $innoCompiler "/DMyAppVersion=$version" (Join-Path $installerDir 'NexaPlay.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

$installerPath = Join-Path $installerDir "output\NexaPlay-Setup-v$version.exe"
if (-not (Test-Path -LiteralPath $installerPath)) { throw "The installer was not created at $installerPath." }
$installer = Get-Item -LiteralPath $installerPath
$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
Write-Host ''
Write-Host 'Installer ready:' -ForegroundColor Green
Write-Host "  $($installer.FullName)"
Write-Host "  Size: $([Math]::Round($installer.Length / 1MB, 2)) MB"
Write-Host "  SHA-256: $($hash.Hash)"
