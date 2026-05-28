param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Configuration = "Release",
    [string]$ApiBaseUrl = "https://krkr.chat",
    [switch]$SelfContained = $true,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$clientProject = Join-Path $repoRoot "src\SecureChat.Desktop\SecureChat.Desktop.csproj"
$publishDir = Join-Path $repoRoot ".artifacts\desktop-client-publish"

if (-not (Test-Path $clientProject)) {
    throw "Desktop client project not found: $clientProject"
}

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    throw "RuntimeIdentifier is required."
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "SecureChat desktop publish configuration" -ForegroundColor Cyan
Write-Host "  Project: $clientProject"
Write-Host "  Configuration: $Configuration"
Write-Host "  RuntimeIdentifier: $RuntimeIdentifier"
Write-Host "  SelfContained: $SelfContained"
Write-Host "  Output: $publishDir"

if ($ValidateOnly) {
    Write-Host "Validation only. Desktop client was not published." -ForegroundColor Yellow
    exit 0
}

Set-Location $repoRoot

dotnet publish $clientProject `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained:$SelfContained `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishTrimmed=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

if (-not [string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $appSettingsPath = Join-Path $publishDir "appsettings.client.json"
    if (Test-Path $appSettingsPath) {
        Write-Host "Applying API base URL: $ApiBaseUrl" -ForegroundColor Cyan
        $json = Get-Content -Raw $appSettingsPath | ConvertFrom-Json
        if ($null -eq $json.Client) {
            $json | Add-Member -NotePropertyName Client -NotePropertyValue ([pscustomobject]@{})
        }

        $json.Client.ApiBaseUrl = $ApiBaseUrl
        $json | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $appSettingsPath
    }
}

Write-Host "Done. Desktop client publish output:" -ForegroundColor Green
Get-ChildItem $publishDir | Select-Object Name, Length
