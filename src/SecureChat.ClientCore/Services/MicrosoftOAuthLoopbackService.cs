using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SecureChat.ClientCore.Services;

public sealed class MicrosoftOAuthLoopbackService
{
    private readonly ApiClient _apiClient;

    public MicrosoftOAuthLoopbackService(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<MicrosoftOAuthLoginResult> SignInAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var callbackPort = GetAvailableLoopbackPort();
        var callbackUri = $"http://127.0.0.1:{callbackPort}/oauth/callback/";

        using var callbackListener = new HttpListener();
        callbackListener.Prefixes.Add(callbackUri);
        callbackListener.Start();

        var signInUrl = _apiClient.BuildMicrosoftLoginUrl(callbackUri);
        Process.Start(new ProcessStartInfo
        {
            FileName = signInUrl,
            UseShellExecute = true
        });

        var callbackTask = callbackListener.GetContextAsync();
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var completedTask = await Task.WhenAny(callbackTask, Task.Delay(effectiveTimeout, cancellationToken));
        if (completedTask != callbackTask)
        {
            throw new TimeoutException($"Sign in timed out after {effectiveTimeout.TotalSeconds:0} seconds.");
        }

        var callbackContext = await callbackTask;
        var query = ParseQueryString(callbackContext.Request.Url?.Query);

        if (!query.TryGetValue("token", out var accessToken) || string.IsNullOrWhiteSpace(accessToken))
        {
            await WriteCallbackPageAsync(callbackContext.Response, "Sign in failed: token missing.");
            throw new InvalidOperationException("Sign in failed: token missing.");
        }

        query.TryGetValue("userId", out var userId);
        await WriteCallbackPageAsync(callbackContext.Response, "Sign in succeeded. You can close this page and return to SecureChat.");

        return new MicrosoftOAuthLoginResult(accessToken, userId ?? string.Empty);
    }

    private static async Task WriteCallbackPageAsync(HttpListenerResponse response, string message)
    {
        var html =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>SecureChat Sign-In</title></head><body>" +
            $"<h2>{WebUtility.HtmlEncode(message)}</h2>" +
            "<p>You can close this page now.</p></body></html>";

        var buffer = Encoding.UTF8.GetBytes(html);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.LongLength;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    private static Dictionary<string, string> ParseQueryString(string? query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return values;
        }

        var content = query[0] == '?' ? query[1..] : query;
        foreach (var segment in content.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = segment.IndexOf('=');
            if (index < 0)
            {
                values[Uri.UnescapeDataString(segment)] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(segment[..index]);
            var value = Uri.UnescapeDataString(segment[(index + 1)..]);
            values[key] = value;
        }

        return values;
    }

    private static int GetAvailableLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}