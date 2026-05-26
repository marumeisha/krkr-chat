param(
    [string]$TunnelName = "securechat",
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"

$cloudflaredCommand = Get-Command cloudflared -ErrorAction SilentlyContinue
if ($null -ne $cloudflaredCommand) {
    $cloudflaredPath = $cloudflaredCommand.Source
}
else {
    $defaultPath = "D:\tools\cloudflared\cloudflared.exe"
    if (-not (Test-Path $defaultPath)) {
        throw "cloudflared.exe not found in PATH or at $defaultPath"
    }

    $cloudflaredPath = $defaultPath
}

$configPath = Join-Path $env:USERPROFILE ".cloudflared\config.yml"
if (-not (Test-Path $configPath)) {
    throw "Tunnel config not found: $configPath"
}

$configText = Get-Content -Raw $configPath
if ($configText -notmatch "hostname:\s*krkr\.chat") {
    throw "Tunnel config does not contain hostname krkr.chat"
}

if ($configText -notmatch "service:\s*http://localhost:5000") {
    throw "Tunnel config does not point to http://localhost:5000"
}

Write-Host "SecureChat tunnel configuration" -ForegroundColor Cyan
Write-Host "  cloudflared: $cloudflaredPath"
Write-Host "  config: $configPath"
Write-Host "  tunnel: $TunnelName"

if ($ValidateOnly) {
    Write-Host "Validation only. Tunnel was not started." -ForegroundColor Yellow
    exit 0
}

& $cloudflaredPath tunnel run $TunnelName