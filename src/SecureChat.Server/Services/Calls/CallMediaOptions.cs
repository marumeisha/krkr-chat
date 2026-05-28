using SecureChat.Shared.Contracts.Calls;

namespace SecureChat.Server.Services.Calls;

public sealed class CallMediaOptions
{
    public const string SectionName = "Calls";

    public List<IceServerDto> IceServers { get; init; } =
    [
        new IceServerDto
        {
            Urls = ["stun:stun.cloudflare.com:3478", "stun:stun.l.google.com:19302"]
        }
    ];
}