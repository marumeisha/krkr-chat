namespace SecureChat.Desktop.Models;

public sealed record MessageAttachmentPayload
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public byte[] ContentBytes { get; init; } = [];
    public long ByteLength { get; init; }
}

public sealed record MessageContentPayload
{
    public string Text { get; init; } = string.Empty;
    public MessageAttachmentPayload? Attachment { get; init; }
}