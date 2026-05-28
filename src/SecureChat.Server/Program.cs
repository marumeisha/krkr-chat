using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using SecureChat.Server.Auth;
using SecureChat.Server.Services.Calls;
using SecureChat.Server.Services.Live;
using SecureChat.Server.Services;
using SecureChat.Server.Services.Online;
using SecureChat.Server.Services.Runtime;
using SecureChat.Shared.Constants;
using System.Net;
using System.Security.Authentication;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MicrosoftOAuthOptions>(builder.Configuration.GetSection(MicrosoftOAuthOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<CloudflareTunnelOptions>(builder.Configuration.GetSection(CloudflareTunnelOptions.SectionName));
builder.Services.Configure<CallMediaOptions>(builder.Configuration.GetSection(CallMediaOptions.SectionName));
builder.Services.Configure<CallCleanupOptions>(builder.Configuration.GetSection(CallCleanupOptions.SectionName));
builder.Services.Configure<LiveRoomCleanupOptions>(builder.Configuration.GetSection(LiveRoomCleanupOptions.SectionName));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            NameClaimType = "unique_name"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("MicrosoftOAuth")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        SslOptions =
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }
    });
builder.Services.AddSingleton<MessageService>();
builder.Services.AddSingleton<PublicKeyDirectoryService>();
builder.Services.AddSingleton<UserAccountService>();
builder.Services.AddSingleton<OnlinePresenceService>();
builder.Services.AddSingleton<CallSignalingService>();
builder.Services.AddHostedService<CallCleanupService>();
builder.Services.AddSingleton<LiveRoomService>();
builder.Services.AddSingleton<LiveRoomSignalingService>();
builder.Services.AddHostedService<LiveRoomCleanupService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<CloudflareTunnelManager>();

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet(ApiRoutes.CallSignalWebSocket, async (HttpContext httpContext, string callId, CallSignalingService callSignalingService, CancellationToken cancellationToken) =>
{
    if (!httpContext.WebSockets.IsWebSocketRequest)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var userId = httpContext.Request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(userId))
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
    await callSignalingService.AttachWebSocketAsync(callId, userId, webSocket, cancellationToken);
});
app.MapGet(ApiRoutes.LiveRoomSignalWebSocket, async (HttpContext httpContext, string roomId, LiveRoomSignalingService liveRoomSignalingService, CancellationToken cancellationToken) =>
{
    if (!httpContext.WebSockets.IsWebSocketRequest)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var userId = httpContext.Request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(userId))
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
    await liveRoomSignalingService.AttachWebSocketAsync(roomId, userId, webSocket, cancellationToken);
});
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { name = "SecureChat.Server", status = "ok" }));

app.Run();
