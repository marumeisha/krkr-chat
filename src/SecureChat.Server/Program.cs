var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<SecureChat.Server.Services.MessageService>();
builder.Services.AddSingleton<SecureChat.Server.Services.PublicKeyDirectoryService>();

var app = builder.Build();

app.MapControllers();
app.MapGet("/", () => Results.Ok(new { name = "SecureChat.Server", status = "ok" }));

app.Run();
