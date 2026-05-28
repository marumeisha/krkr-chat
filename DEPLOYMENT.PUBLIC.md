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

For cross-device WebRTC connectivity, configure ICE servers on the server host.
At minimum keep a STUN server; for reliable public-network calls, add your own TURN server.

Example `appsettings.json` section:

```json
{
  "Calls": {
    "IceServers": [
      {
        "Urls": [ "stun:stun.cloudflare.com:3478", "stun:stun.l.google.com:19302" ]
      },
      {
        "Urls": [ "turn:turn.example.com:3478?transport=udp", "turn:turn.example.com:3478?transport=tcp" ],
        "Username": "<turn-username>",
        "Credential": "<turn-password>"
      }
    ]
  }
}
```

Then run server:

```powershell
.\publish\server\SecureChat.Server.exe
```

When running locally, the server also exposes a built-in operations UI at `http://localhost:5000/`.
It shows service readiness, online users, and Cloudflare Tunnel state. Starting Cloudflare from the page is restricted to localhost requests.

For local source-based startup, this repo also includes a helper:

```powershell
Set-Location D:\github\2\krkr-chat
.\scripts\start-server.ps1
```

Double-click alternative:

- `.\scripts\start-server.bat`

For local source-based startup in this repository, you can also use:

```powershell
Set-Location D:\github\2\krkr-chat
.\scripts\start-server.ps1
```

## 3. Public HTTPS endpoint

Place a reverse proxy (Nginx/Caddy/IIS) in front of port 5000.
- Terminate TLS at the proxy
- Forward traffic to `http://127.0.0.1:5000`
- Keep a stable public domain, for example `https://chat.example.com`

For the local `krkr.chat` setup used during development, start the tunnel with:

```powershell
Set-Location D:\github\2\krkr-chat
.\scripts\start-tunnel.ps1
```

Or start it from the local operations UI at `http://localhost:5000/` after the server is up.

Double-click alternative:

- `.\scripts\start-tunnel.bat`

To start the client against `krkr.chat` from source:

```powershell
Set-Location D:\github\2\krkr-chat
.\scripts\start-client.ps1
```

To start the Avalonia desktop client against `krkr.chat` from source:

```powershell
Set-Location D:\github\2\krkr-chat
.\scripts\start-desktop-client.ps1
```

To publish the Avalonia desktop client:

```powershell
Set-Location D:\github\2\krkr-chat
.\scripts\publish-desktop-client.ps1 -ApiBaseUrl "https://krkr.chat"
```

Default publish output:

- `.artifacts\desktop-client-publish`

Double-click alternative:

- `.\scripts\start-client.bat`
- `\.\scripts\start-desktop-client.bat`
- `.\scripts\publish-desktop-client.bat`

If you use Cloudflare Tunnel locally, start it with:

```powershell
Set-Location D:\github\2\krkr-chat
.\scripts\start-tunnel.ps1
```

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

## 5.2 Build Avalonia desktop installer (.exe)

The Avalonia desktop client has a separate Inno Setup script and build helper.

1) Install Inno Setup 6

- Download: https://jrsoftware.org/isdl.php

2) Build desktop installer

```powershell
Set-Location D:\github\2\krkr-chat
.\scripts\build-desktop-client-installer.ps1 -Version 1.0.0 -ApiBaseUrl "https://chat.example.com"
```

Output file:

- `.artifacts\installer\SecureChat.Desktop.Setup.exe`

Notes:

- The script publishes `win-x64` self-contained desktop client, then compiles installer with Inno Setup.
- If you already ran `.\scripts\publish-desktop-client.ps1`, you can reuse the existing publish output with `-NoPublish`.

Double-click alternative:

- `.\scripts\build-desktop-client-installer.bat`

## 6. Security checklist

- Revoke any leaked Microsoft client secret and create a new one
- Never commit real secrets to source control
- Use host firewall rules to allow only required ports
- Use a strong JWT signing key and rotate it periodically
- For public-network calling, prefer your own TURN server instead of depending on STUN only
