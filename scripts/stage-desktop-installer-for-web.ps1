param(
    [string]$InstallerPath = "",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$defaultInstallerDir = Join-Path $repoRoot ".artifacts\installer"
$webDownloadsDir = Join-Path $repoRoot "src\SecureChat.Server\wwwroot\downloads"
$canonicalInstallerName = "SecureChat.Desktop.Setup.exe"
$canonicalInstallerPath = Join-Path $webDownloadsDir $canonicalInstallerName
$metadataPath = Join-Path $webDownloadsDir "latest.json"

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $candidate = Get-ChildItem -Path $defaultInstallerDir -Filter "SecureChat.Desktop.Setup*.exe" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw "No desktop installer was found under $defaultInstallerDir. Build one first with scripts\build-desktop-client-installer.ps1."
    }

    $InstallerPath = $candidate.FullName
}

$resolvedInstallerPath = (Resolve-Path $InstallerPath).Path
if (-not (Test-Path $resolvedInstallerPath)) {
    throw "Installer not found: $InstallerPath"
}

New-Item -ItemType Directory -Path $webDownloadsDir -Force | Out-Null
Copy-Item $resolvedInstallerPath $canonicalInstallerPath -Force

$fileInfo = Get-Item $canonicalInstallerPath
$hash = Get-FileHash $canonicalInstallerPath -Algorithm SHA256
$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($canonicalInstallerPath).FileVersion

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = if ([string]::IsNullOrWhiteSpace($fileVersion)) { "unknown" } else { $fileVersion }
}

$metadata = [ordered]@{
    version = $Version
    sizeBytes = $fileInfo.Length
    sha256 = $hash.Hash.ToLowerInvariant()
    updatedUtc = [DateTime]::UtcNow.ToString("O")
    downloadPath = "/downloads/$canonicalInstallerName"
}

$metadata | ConvertTo-Json | Set-Content -Path $metadataPath -Encoding UTF8

Write-Host "Installer staged for web download:" -ForegroundColor Green
Write-Host "  File: $canonicalInstallerPath"
Write-Host "  Metadata: $metadataPath"
Write-Host "  Version: $Version"
Write-Host "  Size: $($fileInfo.Length) bytes"
Write-Host "  SHA-256: $($hash.Hash.ToLowerInvariant())"