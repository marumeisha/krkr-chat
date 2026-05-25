# SecureChat Public Deployment Guide

## 1. Server publish

```powershell
Set-Location D:\github\2\krkr-chat
dotnet publish .\src\SecureChat.Server\SecureChat.Server.csproj -c Release -o .\publish\server
```

## 2. Server runtime settings

Use environment variables on the server host.

```powershell
$env:ASPNETCORE_URLS = "http://0.0.0.0:5000"
$env:Authentication__Microsoft__ClientId = "<your-client-id>"
$env:Authentication__Microsoft__ClientSecret = "<your-client-secret>"
$env:Authentication__Microsoft__Tenant = "common"
$env:Authentication__Microsoft__CallbackPath = "/api/auth/oauth/microsoft/callback"
$env:Authentication__Jwt__SigningKey = "<long-random-key-at-least-32-chars>"
```

Then run server:

```powershell
.\publish\server\SecureChat.Server.exe
```

## 3. Public HTTPS endpoint

Place a reverse proxy (Nginx/Caddy/IIS) in front of port 5000.
- Terminate TLS at the proxy
- Forward traffic to `http://127.0.0.1:5000`
- Keep a stable public domain, for example `https://chat.example.com`

## 4. Microsoft Entra app registration

In Authentication -> Web redirect URI, add:

- `https://chat.example.com/api/auth/oauth/microsoft/callback`

If you keep local debug, also keep:

- `http://localhost:5000/api/auth/oauth/microsoft/callback`

## 5. Client publish

```powershell
Set-Location D:\github\2\krkr-chat
dotnet publish .\src\SecureChat.Client\SecureChat.Client.csproj -c Release -o .\publish\client
```

Edit `publish\client\appsettings.client.json`:

```json
{
  "Client": {
    "ApiBaseUrl": "https://chat.example.com"
  }
}
```

Or override per device:

```powershell
$env:SECURECHAT_API_BASE_URL = "https://chat.example.com"
.\publish\client\SecureChat.Client.exe
```

## 5.1 Build Windows installer (.exe)

This project includes an Inno Setup script and a one-command build helper.

1) Install Inno Setup 6

- Download: https://jrsoftware.org/isdl.php

2) Build installer

```powershell
Set-Location D:\github\2\krkr-chat
.\scripts\build-client-installer.ps1 -Version 1.0.0 -ApiBaseUrl "https://chat.example.com"
```

Output file:

- `.artifacts\installer\SecureChat.Client.Setup.exe`

Notes:

- The script publishes `win-x64` self-contained client, then compiles installer with Inno Setup.
- If you do not pass `-ApiBaseUrl`, installed client keeps default value from `appsettings.client.json`.

## 6. Security checklist

- Revoke any leaked Microsoft client secret and create a new one
- Never commit real secrets to source control
- Use host firewall rules to allow only required ports
- Use a strong JWT signing key and rotate it periodically
