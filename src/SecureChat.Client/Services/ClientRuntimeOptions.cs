using System.Text.Json;

namespace SecureChat.Client.Services;

public sealed class ClientRuntimeOptions
{
    public Uri ApiBaseUri { get; init; } = new("http://localhost:5000");

    public static ClientRuntimeOptions Load()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SECURECHAT_API_BASE_URL");
        if (TryParseHttpBaseUri(fromEnv, out var envUri))
        {
            return new ClientRuntimeOptions { ApiBaseUri = envUri! };
        }

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.client.json");
        if (!File.Exists(configPath))
        {
            return new ClientRuntimeOptions();
        }

        try
        {
            using var stream = File.OpenRead(configPath);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.TryGetProperty("Client", out var clientSection)
                && clientSection.TryGetProperty("ApiBaseUrl", out var apiBaseUrlElement)
                && apiBaseUrlElement.ValueKind == JsonValueKind.String
                && TryParseHttpBaseUri(apiBaseUrlElement.GetString(), out var fileUri))
            {
                return new ClientRuntimeOptions { ApiBaseUri = fileUri! };
            }
        }
        catch
        {
            // Fallback to default when config is malformed.
        }

        return new ClientRuntimeOptions();
    }

    private static bool TryParseHttpBaseUri(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
