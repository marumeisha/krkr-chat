using System.Security.Cryptography;
using System.Text;
using SecureChat.Client.Services;
using SecureChat.Core.Crypto;
using SecureChat.Core.Keys;
using SecureChat.Core.Models;
using SecureChat.Shared.Contracts.Messages;

namespace SecureChat.Client;

public sealed class MainForm : Form
{
    private readonly TextBox _currentUserTextBox = new() { PlaceholderText = "Current User ID", Width = 180 };
    private readonly TextBox _recipientUserTextBox = new() { PlaceholderText = "Recipient User ID", Width = 180 };
    private readonly TextBox _messageTextBox = new() { Multiline = true, Width = 520, Height = 120, ScrollBars = ScrollBars.Vertical };
    private readonly Button _registerKeyButton = new() { Text = "Register My Public Key", Width = 160 };
    private readonly Button _sendButton = new() { Text = "Encrypt && Send", Width = 120 };
    private readonly Button _refreshButton = new() { Text = "Refresh Inbox", Width = 120 };
    private readonly ListBox _messagesListBox = new() { Width = 860, Height = 260 };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Ready" };

    private readonly ApiClient _apiClient;
    private readonly IdentityKeyService _identityKeyService;
    private readonly IdentityBootstrapService _identityBootstrapService;
    private readonly MessageCryptoService _messageCryptoService = new();

    public MainForm()
    {
        Text = "SecureChat Client";
        Width = 920;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;

        var keyStore = new WindowsDpapiKeyStore();
        _identityKeyService = new IdentityKeyService(keyStore);
        _identityBootstrapService = new IdentityBootstrapService(_identityKeyService);
        _apiClient = new ApiClient(new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5199")
        });

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
            WrapContents = true
        };

        topPanel.Controls.Add(_currentUserTextBox);
        topPanel.Controls.Add(_recipientUserTextBox);
        topPanel.Controls.Add(_registerKeyButton);
        topPanel.Controls.Add(_sendButton);
        topPanel.Controls.Add(_refreshButton);

        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        _messageTextBox.Top = 10;
        _messageTextBox.Left = 10;
        _messagesListBox.Top = 150;
        _messagesListBox.Left = 10;
        _statusLabel.Top = 420;
        _statusLabel.Left = 10;

        contentPanel.Controls.Add(_messageTextBox);
        contentPanel.Controls.Add(_messagesListBox);
        contentPanel.Controls.Add(_statusLabel);

        Controls.Add(contentPanel);
        Controls.Add(topPanel);

        _registerKeyButton.Click += async (_, _) => await RegisterMyPublicKeyAsync();
        _sendButton.Click += async (_, _) => await SendCurrentMessageAsync();
        _refreshButton.Click += async (_, _) => await RefreshInboxAsync();
    }

    private async Task RegisterMyPublicKeyAsync()
    {
        try
        {
            var userId = _currentUserTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                SetStatus("Please enter Current User ID.");
                return;
            }

            _identityBootstrapService.EnsureInitialized(userId);
            var publicKeyPem = _identityKeyService.LoadPublicKeyPem(userId);
            await _apiClient.RegisterPublicKeyAsync(userId, publicKeyPem);
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
            var senderUserId = _currentUserTextBox.Text.Trim();
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
            var userId = _currentUserTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                SetStatus("Please enter Current User ID.");
                return;
            }

            _identityBootstrapService.EnsureInitialized(userId);
            using var privateKey = _identityKeyService.LoadPrivateKey(userId);

            var inbox = await _apiClient.GetInboxAsync(userId);
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

            SetStatus($"Inbox refreshed: {inbox.Count} message(s).");
        }
        catch (Exception ex)
        {
            SetStatus($"Refresh failed: {ex.Message}");
        }
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private static string BuildConversationId(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}:{b}" : $"{b}:{a}";
    }
}
