param(
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"

function Get-ScopedEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [string]$DefaultValue = ""
    )

    $value = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = [Environment]::GetEnvironmentVariable($Name, "User")
    }

    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = [Environment]::GetEnvironmentVariable($Name, "Machine")
    }

    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = $DefaultValue
    }

    if (-not [string]::IsNullOrWhiteSpace($value)) {
        [Environment]::SetEnvironmentVariable($Name, $value, "Process")
    }

    return $value
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$serverProject = Join-Path $repoRoot "src\SecureChat.Server\SecureChat.Server.csproj"

if (-not (Test-Path $serverProject)) {
    throw "Server project not found: $serverProject"
}

$clientId = Get-ScopedEnvironmentValue -Name "Authentication__Microsoft__ClientId"
$clientSecret = Get-ScopedEnvironmentValue -Name "Authentication__Microsoft__ClientSecret"
$tenant = Get-ScopedEnvironmentValue -Name "Authentication__Microsoft__Tenant" -DefaultValue "common"
$jwtSigningKey = Get-ScopedEnvironmentValue -Name "Authentication__Jwt__SigningKey"
$callbackPath = Get-ScopedEnvironmentValue -Name "Authentication__Microsoft__CallbackPath" -DefaultValue "/api/auth/oauth/microsoft/callback"
$aspNetCoreUrls = Get-ScopedEnvironmentValue -Name "ASPNETCORE_URLS" -DefaultValue "http://0.0.0.0:5000"

$missing = @()
if ([string]::IsNullOrWhiteSpace($clientId)) { $missing += "Authentication__Microsoft__ClientId" }
if ([string]::IsNullOrWhiteSpace($clientSecret)) { $missing += "Authentication__Microsoft__ClientSecret" }
if ([string]::IsNullOrWhiteSpace($jwtSigningKey)) { $missing += "Authentication__Jwt__SigningKey" }

if ($missing.Count -gt 0) {
    throw "Missing required environment variables: $($missing -join ', ')"
}

Write-Host "SecureChat server configuration" -ForegroundColor Cyan
Write-Host "  Project: $serverProject"
Write-Host "  ASPNETCORE_URLS: $aspNetCoreUrls"
Write-Host "  Microsoft ClientId: $clientId"
Write-Host "  Microsoft Tenant: $tenant"
Write-Host "  CallbackPath: $callbackPath"
Write-Host "  ClientSecret present: $(-not [string]::IsNullOrWhiteSpace($clientSecret))"
Write-Host "  JWT signing key present: $(-not [string]::IsNullOrWhiteSpace($jwtSigningKey))"

if ($ValidateOnly) {
    Write-Host "Validation only. Server was not started." -ForegroundColor Yellow
    exit 0
}

Set-Location $repoRoot
dotnet run --project $serverProject