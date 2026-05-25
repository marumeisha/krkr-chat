param(
    [string]$Version = "1.0.0",
    [string]$ApiBaseUrl = "",
    [switch]$NoPublish
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$clientProject = Join-Path $repoRoot "src\SecureChat.Client\SecureChat.Client.csproj"
$issScript = Join-Path $repoRoot "installer\SecureChat.Client.iss"
$publishDir = Join-Path $repoRoot ".artifacts\client-publish"
$outputDir = Join-Path $repoRoot ".artifacts\installer"

if (-not (Test-Path $clientProject)) {
    throw "Client project not found: $clientProject"
}

if (-not (Test-Path $issScript)) {
    throw "Inno Setup script not found: $issScript"
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

if (-not $NoPublish) {
    Write-Host "Publishing client..." -ForegroundColor Cyan
    dotnet publish $clientProject `
        -c Release `
        -r win-x64 `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:PublishTrimmed=false `
        -o $publishDir
}

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

Write-Host "Building installer..." -ForegroundColor Cyan
& $isccPath `
    "/DMyAppVersion=$Version" `
    "/DSourceDir=$publishDir" `
    "/DOutputDir=$outputDir" `
    $issScript

if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed."
}

Write-Host "Done. Installer output:" -ForegroundColor Green
Get-ChildItem $outputDir -Filter "SecureChat.Client.Setup*.exe" | Select-Object FullName, Length
