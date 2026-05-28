using System.Drawing;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using SecureChat.Client.Modules.OnlineUsers;
using SecureChat.ClientCore.Services;
using SecureChat.Core.Crypto;
using SecureChat.Core.Keys;
using SecureChat.Core.Models;
using SecureChat.Shared.Contracts.Messages;

namespace SecureChat.Client;

public sealed class MainForm : Form
{
    private readonly TextBox _customUserIdTextBox = new() { PlaceholderText = "My Custom User ID", Width = 180, Margin = new Padding(4) };
    private readonly TextBox _recipientUserTextBox = new() { PlaceholderText = "Recipient User ID", Width = 180, Margin = new Padding(4) };
    private readonly TextBox _messageTextBox = new() { Multiline = true, Width = 520, Height = 120, ScrollBars = ScrollBars.Vertical };
    private readonly Button _registerKeyButton = new()
    {
        Text = "Register My Public Key",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(4)
    };
    private readonly Button _sendButton = new()
    {
        Text = "Encrypt && Send",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(4)
    };
    private readonly Button _refreshButton = new()
    {
        Text = "Refresh Inbox",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(4)
    };
    private readonly Button _signInButton = new()
    {
        Text = "Sign in with Microsoft",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(4)
    };
    private readonly Button _saveUserIdButton = new()
    {
        Text = "Save My ID",
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(4)
    };
    private readonly ListBox _messagesListBox = new() { Width = 540, Height = 260 };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Ready" };
    private readonly OnlineUsersPanel _onlineUsersPanel = new() { Width = 300, Height = 400 };

    private readonly ApiClient _apiClient;
    private readonly IdentityKeyService _identityKeyService;
    private readonly IdentityBootstrapService _identityBootstrapService;
    private readonly MessageCryptoService _messageCryptoService = new();
    private readonly OnlineUsersService _onlineUsersService = new();
    private readonly System.Windows.Forms.Timer _presenceTimer = new() { Interval = 30_000 };
    private bool _splittersInitialized;
    private string _currentUserId = string.Empty;

    public MainForm(Uri apiBaseUri)
    {
        Text = "SecureChat Client";
        Width = 920;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;
        Font = SystemFonts.MessageBoxFont;

        var keyStore = new WindowsDpapiKeyStore();
        _identityKeyService = new IdentityKeyService(keyStore);
        _identityBootstrapService = new IdentityBootstrapService(_identityKeyService);
        _apiClient = new ApiClient(new HttpClient
        {
            BaseAddress = apiBaseUri
        });

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        topPanel.Controls.Add(_customUserIdTextBox);
        topPanel.Controls.Add(_recipientUserTextBox);
        topPanel.Controls.Add(_signInButton);
        topPanel.Controls.Add(_saveUserIdButton);
        topPanel.Controls.Add(_registerKeyButton);
        topPanel.Controls.Add(_sendButton);
        topPanel.Controls.Add(_refreshButton);

        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        var chatSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        _messageTextBox.Dock = DockStyle.Fill;
        _messagesListBox.Dock = DockStyle.Fill;
        _onlineUsersPanel.Dock = DockStyle.Fill;

        chatSplit.Panel1.Controls.Add(_messageTextBox);
        chatSplit.Panel2.Controls.Add(_messagesListBox);
        mainSplit.Panel1.Controls.Add(chatSplit);
        mainSplit.Panel2.Controls.Add(_onlineUsersPanel);

        _statusLabel.AutoSize = false;
        _statusLabel.Height = 24;
        _statusLabel.Dock = DockStyle.Bottom;

        contentPanel.Controls.Add(mainSplit);
        contentPanel.Controls.Add(_statusLabel);

        Controls.Add(contentPanel);
        Controls.Add(topPanel);

        Shown += (_, _) => EnsureInitialSplitters(mainSplit, chatSplit);

        _signInButton.Click += async (_, _) => await SignInWithMicrosoftAsync();
        _saveUserIdButton.Click += async (_, _) => await SaveCustomUserIdAsync();
        _registerKeyButton.Click += async (_, _) => await RegisterMyPublicKeyAsync();
        _sendButton.Click += async (_, _) => await SendCurrentMessageAsync();
        _refreshButton.Click += async (_, _) => await RefreshInboxAsync();
        _customUserIdTextBox.TextChanged += (_, _) => RefreshOnlineUsersPanel();
        _onlineUsersPanel.UserPicked += userId => _recipientUserTextBox.Text = userId;
        _presenceTimer.Tick += (_, _) => _ = SendPresenceHeartbeatAsync();
        _presenceTimer.Start();

        RefreshOnlineUsersPanel();
    }

    private void EnsureInitialSplitters(SplitContainer mainSplit, SplitContainer chatSplit)
    {
        if (_splittersInitialized)
        {
            return;
        }

        mainSplit.Panel1MinSize = 320;
        mainSplit.Panel2MinSize = 220;
        chatSplit.Panel1MinSize = 90;
        chatSplit.Panel2MinSize = 150;

        if (!TrySetSplitterDistance(mainSplit, 560))
        {
            return;
        }

        if (!TrySetSplitterDistance(chatSplit, 130))
        {
            return;
        }

        _splittersInitialized = true;
    }

    private static bool TrySetSplitterDistance(SplitContainer splitContainer, int desired)
    {
        var maxDistance = splitContainer.Orientation == Orientation.Vertical
            ? splitContainer.Width - splitContainer.Panel2MinSize
            : splitContainer.Height - splitContainer.Panel2MinSize;

        var minDistance = splitContainer.Panel1MinSize;
        if (maxDistance < minDistance)
        {
            return false;
        }

        splitContainer.SplitterDistance = Math.Clamp(desired, minDistance, maxDistance);
        return true;
    }

    private async Task SignInWithMicrosoftAsync()
    {
        try
        {
            _signInButton.Enabled = false;

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

            SetStatus("Browser opened for Microsoft sign in.");

            var callbackTask = callbackListener.GetContextAsync();
            var completedTask = await Task.WhenAny(callbackTask, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completedTask != callbackTask)
            {
                SetStatus("Sign in timed out after 30 seconds. Please try again.");
                return;
            }

            var callbackContext = await callbackTask;
            var query = ParseQueryString(callbackContext.Request.Url?.Query);

            if (!query.TryGetValue("token", out var accessToken) || string.IsNullOrWhiteSpace(accessToken))
            {
                await WriteCallbackPageAsync(callbackContext.Response, "Sign in failed: token missing.");
                SetStatus("Sign in failed: token missing.");
                return;
            }

            _apiClient.SetBearerToken(accessToken);

            if (query.TryGetValue("userId", out var userId) && !string.IsNullOrWhiteSpace(userId))
            {
                SetCurrentUserId(userId);
            }

            await WriteCallbackPageAsync(callbackContext.Response, "Sign in succeeded. You can close this page and return to SecureChat Client.");

            var currentUser = await _apiClient.GetCurrentUserAsync();
            if (currentUser is not null)
            {
                if (!string.IsNullOrWhiteSpace(currentUser.UserId))
                {
                    SetCurrentUserId(currentUser.UserId);
                }

                RefreshOnlineUsersPanel();
                SetStatus($"Signed in as {currentUser.DisplayName} ({currentUser.UserId}).");
                _ = SendPresenceHeartbeatAsync();
                return;
            }

            SetStatus("Signed in, but user profile could not be loaded.");
        }
        catch (HttpListenerException ex)
        {
            SetStatus($"Unable to start callback listener: {ex.Message}");
        }
        catch (Exception ex)
        {
            SetStatus($"Sign in failed: {ex.Message}");
        }
        finally
        {
            _signInButton.Enabled = true;
        }
    }

    private async Task RegisterMyPublicKeyAsync()
    {
        try
        {
            var userId = _currentUserId.Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                SetStatus("Please enter Current User ID.");
                return;
            }

            _identityBootstrapService.EnsureInitialized(userId);
            var publicKeyPem = _identityKeyService.LoadPublicKeyPem(userId);
            await _apiClient.RegisterPublicKeyAsync(userId, publicKeyPem);
            _onlineUsersService.RecordSeen(userId);
            RefreshOnlineUsersPanel();
            SetStatus("Public key registered.");
        }
        catch (Exception ex)
        {
            SetStatus($"Register key failed: {ex.Message}");
        }
    }

    private async Task SendCurrentMessageAsync()
    {
        try
        {
            var senderUserId = _currentUserId.Trim();
            var recipientUserId = _recipientUserTextBox.Text.Trim();
            var plaintext = _messageTextBox.Text;

            if (string.IsNullOrWhiteSpace(senderUserId) || string.IsNullOrWhiteSpace(recipientUserId) || string.IsNullOrWhiteSpace(plaintext))
            {
                SetStatus("Current User ID, Recipient User ID, and message text are required.");
                return;
            }

            _identityBootstrapService.EnsureInitialized(senderUserId);

            var recipientPublicKeyPem = await _apiClient.GetPublicKeyAsync(recipientUserId);
            if (string.IsNullOrWhiteSpace(recipientPublicKeyPem))
            {
                SetStatus("Recipient public key not found. Ask them to register first.");
                return;
            }

            using var recipientRsa = _identityKeyService.LoadPublicKeyFromPem(recipientPublicKeyPem);
            using var senderPrivateKey = _identityKeyService.LoadPrivateKey(senderUserId);
            var senderPublicKeyPem = _identityKeyService.LoadPublicKeyPem(senderUserId);

            var metadata = new ChatMessageMetadata
            {
                MessageId = Guid.NewGuid().ToString("N"),
                ConversationId = BuildConversationId(senderUserId, recipientUserId),
                SenderUserId = senderUserId,
                SenderDeviceId = Environment.MachineName,
                RecipientUserId = recipientUserId,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageType = "text"
            };

            var envelope = _messageCryptoService.EncryptText(
                plaintext,
                metadata,
                recipientRsa,
                senderPrivateKey,
                senderPublicKeyPem);

            var envelopeJson = _messageCryptoService.SerializeEnvelope(envelope);

            await _apiClient.SendMessageAsync(new SendMessageRequest
            {
                SenderUserId = senderUserId,
                RecipientUserId = recipientUserId,
                EnvelopeJson = envelopeJson
            });

            _onlineUsersService.RecordSeen(senderUserId);
            _onlineUsersService.RecordSeen(recipientUserId);
            RefreshOnlineUsersPanel();
            _messageTextBox.Clear();
            SetStatus("Encrypted message sent.");
        }
        catch (Exception ex)
        {
            SetStatus($"Send failed: {ex.Message}");
        }
    }

    private async Task RefreshInboxAsync()
    {
        try
        {
            var userId = _currentUserId.Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                SetStatus("Please enter Current User ID.");
                return;
            }

            _identityBootstrapService.EnsureInitialized(userId);
            using var privateKey = _identityKeyService.LoadPrivateKey(userId);

            var inbox = await _apiClient.GetInboxAsync(userId);
            _onlineUsersService.RecordSeen(userId);
            _onlineUsersService.RecordInbox(inbox);
            _messagesListBox.Items.Clear();

            foreach (var item in inbox)
            {
                var envelope = _messageCryptoService.DeserializeEnvelope(item.EnvelopeJson);
                var decrypted = _messageCryptoService.DecryptText(envelope, privateKey);
                var text = decrypted.Success
                    ? $"[{item.CreatedAt:yyyy-MM-dd HH:mm:ss}] {item.SenderUserId}: {decrypted.Plaintext} (sig: {(decrypted.SignatureValid ? "ok" : "invalid")})"
                    : $"[{item.CreatedAt:yyyy-MM-dd HH:mm:ss}] {item.SenderUserId}: <decrypt failed> {decrypted.Error}";

                _messagesListBox.Items.Add(text);
            }

            RefreshOnlineUsersPanel();
            SetStatus($"Inbox refreshed: {inbox.Count} message(s).");
        }
        catch (Exception ex)
        {
            SetStatus($"Refresh failed: {ex.Message}");
        }
    }

    private async Task SaveCustomUserIdAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentUserId))
            {
                SetStatus("Please sign in before changing your user ID.");
                return;
            }

            var requestedUserId = _customUserIdTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(requestedUserId))
            {
                SetStatus("Please enter a custom user ID.");
                return;
            }

            var previousUserId = _currentUserId;
            var updatedUser = await _apiClient.UpdateMyUserIdAsync(requestedUserId);
            if (!string.Equals(previousUserId, updatedUser.UserId, StringComparison.OrdinalIgnoreCase))
            {
                _identityKeyService.RenameIdentity(previousUserId, updatedUser.UserId);
            }

            SetCurrentUserId(updatedUser.UserId);
            SetStatus($"User ID updated to {updatedUser.UserId}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Update user ID failed: {ex.Message}");
        }
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private void RefreshOnlineUsersPanel()
    {
        var currentUserId = _currentUserId.Trim();
        var entries = _onlineUsersService.GetEntries();
        _onlineUsersPanel.SetEntries(entries, currentUserId);
        _ = RefreshOnlineUsersFromServerAsync(currentUserId);
    }

    private void SetCurrentUserId(string userId)
    {
        _currentUserId = userId.Trim();
        _customUserIdTextBox.Text = _currentUserId;
        _onlineUsersService.RecordSeen(_currentUserId);
        RefreshOnlineUsersPanel();
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

    private static string BuildConversationId(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}:{b}" : $"{b}:{a}";
    }

    private async Task SendPresenceHeartbeatAsync()
    {
        var userId = _currentUserId.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        try
        {
            await _apiClient.SendHeartbeatAsync(userId, Environment.MachineName);
            await RefreshOnlineUsersFromServerAsync(userId);
        }
        catch
        {
            // Heartbeat is best-effort and should not interrupt chat flow.
        }
    }

    private async Task RefreshOnlineUsersFromServerAsync(string currentUserId)
    {
        try
        {
            var stats = await _apiClient.GetOnlineStatsAsync();
            if (stats is null)
            {
                return;
            }

            var entries = stats.Users
                .Select(user => new OnlineUserEntry
                {
                    UserId = user.UserId,
                    LastSeenAt = user.LastSeenUtc,
                    IsOnline = true
                })
                .ToList();

            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(() => _onlineUsersPanel.SetEntries(entries, currentUserId));
        }
        catch
        {
            // Keep local fallback list when server stats are temporarily unavailable.
        }
    }
}
