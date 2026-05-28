param(
    [string]$Version = "1.0.0",
    [string]$ApiBaseUrl = "https://krkr.chat",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release",
    [string]$FfmpegDir = "",
    [switch]$SelfContained = $true,
    [switch]$NoPublish
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$clientProject = Join-Path $repoRoot "src\SecureChat.Desktop\SecureChat.Desktop.csproj"
$publishScript = Join-Path $repoRoot "scripts\publish-desktop-client.ps1"
$issScript = Join-Path $repoRoot "installer\SecureChat.Desktop.iss"
$publishDir = Join-Path $repoRoot ".artifacts\desktop-client-publish"
$outputDir = Join-Path $repoRoot ".artifacts\installer"

function Get-FfmpegProbeDirectories {
    param(
        [string]$RepoRoot,
        [string]$PublishDir,
        [string]$ConfiguredDir
    )

    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredDir)) {
        $candidates += $ConfiguredDir
    }

    $candidates += @(
        $PublishDir,
        (Join-Path $PublishDir "ffmpeg"),
        (Join-Path $PublishDir "ffmpeg\bin"),
        (Join-Path $RepoRoot "src\SecureChat.Desktop\bin\Debug\net8.0-windows"),
        (Join-Path $RepoRoot "src\SecureChat.Desktop\bin\Release\net8.0-windows\win-x64"),
        (Join-Path $RepoRoot "src\SecureChat.Desktop\bin\Release\net8.0\win-x64")
    )

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $fullPath = [System.IO.Path]::GetFullPath($candidate)
            if ($seen.Add($fullPath)) {
                $fullPath
            }
        }
    }
}

function Test-FfmpegLibraryDirectory {
    param([string]$Directory)

    if (-not (Test-Path $Directory)) {
        return $false
    }

    foreach ($pattern in @("avcodec*.dll", "avformat*.dll", "avutil*.dll")) {
        if (-not (Get-ChildItem -Path $Directory -Filter $pattern -ErrorAction SilentlyContinue | Select-Object -First 1)) {
            return $false
        }
    }

    return $true
}

function Sync-FfmpegLibraries {
    param(
        [string]$SourceDir,
        [string]$DestinationDir
    )

    $patterns = @(
        "avcodec*.dll",
        "avdevice*.dll",
        "avfilter*.dll",
        "avformat*.dll",
        "avutil*.dll",
        "swresample*.dll",
        "swscale*.dll",
        "postproc*.dll",
        "ffmpeg.exe",
        "ffplay.exe",
        "ffprobe.exe"
    )

    foreach ($pattern in $patterns) {
        Get-ChildItem -Path $SourceDir -Filter $pattern -ErrorAction SilentlyContinue | ForEach-Object {
            $destinationPath = Join-Path $DestinationDir $_.Name
            if ([System.StringComparer]::OrdinalIgnoreCase.Equals($_.FullName, $destinationPath)) {
                return
            }

            Copy-Item $_.FullName -Destination $destinationPath -Force
        }
    }
}

function Ensure-DesktopAppSettings {
    param(
        [string]$RepoRoot,
        [string]$PublishDir,
        [string]$ApiBaseUrl
    )

    $sourceAppSettings = Join-Path $RepoRoot "src\SecureChat.Desktop\appsettings.client.json"
    $targetAppSettings = Join-Path $PublishDir "appsettings.client.json"

    if (-not (Test-Path $targetAppSettings)) {
        if (-not (Test-Path $sourceAppSettings)) {
            throw "Desktop appsettings file not found: $sourceAppSettings"
        }

        Copy-Item $sourceAppSettings $targetAppSettings -Force
    }

    if (-not [string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
        $json = Get-Content -Raw $targetAppSettings | ConvertFrom-Json
        if ($null -eq $json.Client) {
            $json | Add-Member -NotePropertyName Client -NotePropertyValue ([pscustomobject]@{})
        }

        $json.Client.ApiBaseUrl = $ApiBaseUrl
        $json | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $targetAppSettings
    }
}

if (-not (Test-Path $clientProject)) {
    throw "Desktop client project not found: $clientProject"
}

if (-not (Test-Path $publishScript)) {
    throw "Desktop publish script not found: $publishScript"
}

if (-not (Test-Path $issScript)) {
    throw "Inno Setup script not found: $issScript"
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

if (-not $NoPublish) {
    Write-Host "Publishing desktop client..." -ForegroundColor Cyan

    $publishArgs = @{
        FilePath = $publishScript
        RuntimeIdentifier = $RuntimeIdentifier
        Configuration = $Configuration
        SelfContained = $SelfContained
    }

    if (-not [string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
        $publishArgs.ApiBaseUrl = $ApiBaseUrl
    }

    & $publishScript @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop publish failed."
    }
}

$desktopExe = Join-Path $publishDir "SecureChat.Desktop.exe"
if (-not (Test-Path $desktopExe)) {
    throw "Desktop publish output missing: $desktopExe"
}

Ensure-DesktopAppSettings -RepoRoot $repoRoot -PublishDir $publishDir -ApiBaseUrl $ApiBaseUrl

$ffmpegSourceDir = Get-FfmpegProbeDirectories -RepoRoot $repoRoot -PublishDir $publishDir -ConfiguredDir $FfmpegDir |
    Where-Object { Test-FfmpegLibraryDirectory $_ } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($ffmpegSourceDir)) {
    throw "FFmpeg shared DLLs not found. Put them in the desktop publish directory, pass -FfmpegDir, or stage them under src\\SecureChat.Desktop\\bin\\Debug\\net8.0-windows."
}

Write-Host "Using FFmpeg shared libraries from: $ffmpegSourceDir" -ForegroundColor Cyan
Sync-FfmpegLibraries -SourceDir $ffmpegSourceDir -DestinationDir $publishDir

$isccCommand = Get-Command iscc -ErrorAction SilentlyContinue
if ($null -eq $isccCommand) {
    $defaultIscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (Test-Path $defaultIscc) {
        $isccPath = $defaultIscc
    }
    else {
        throw "ISCC not found. Install Inno Setup 6 first: https://jrsoftware.org/isdl.php"
    }
}
else {
    $isccPath = $isccCommand.Source
}

Write-Host "Building desktop installer..." -ForegroundColor Cyan
& $isccPath `
    "/DMyAppVersion=$Version" `
    "/DSourceDir=$publishDir" `
    "/DOutputDir=$outputDir" `
    $issScript

if ($LASTEXITCODE -ne 0) {
    throw "Desktop installer build failed."
}

Write-Host "Done. Desktop installer output:" -ForegroundColor Green
Get-ChildItem $outputDir -Filter "SecureChat.Desktop.Setup*.exe" | Select-Object FullName, Length