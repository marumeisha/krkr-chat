using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using SecureChat.Server.Auth;
using SecureChat.Server.Services;
using SecureChat.Server.Services.Online;
using System.Net;
using System.Security.Authentication;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MicrosoftOAuthOptions>(builder.Configuration.GetSection(MicrosoftOAuthOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

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
builder.Services.AddSingleton<JwtTokenService>();

var app = builder.Build();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => Results.Ok(new { name = "SecureChat.Server", status = "ok" }));

app.Run();
