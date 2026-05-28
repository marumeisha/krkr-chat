param(
    [string]$ApiBaseUrl = "https://krkr.chat",
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$clientProject = Join-Path $repoRoot "src\SecureChat.Desktop\SecureChat.Desktop.csproj"

if (-not (Test-Path $clientProject)) {
    throw "Desktop client project not found: $clientProject"
}

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    throw "ApiBaseUrl is required."
}

$env:SECURECHAT_API_BASE_URL = $ApiBaseUrl

Write-Host "SecureChat desktop client configuration" -ForegroundColor Cyan
Write-Host "  Project: $clientProject"
Write-Host "  SECURECHAT_API_BASE_URL: $env:SECURECHAT_API_BASE_URL"

if ($ValidateOnly) {
    Write-Host "Validation only. Desktop client was not started." -ForegroundColor Yellow
    exit 0
}

Set-Location $repoRoot
dotnet run --project $clientProject
