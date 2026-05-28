using SIPSorceryMedia.Abstractions;

namespace SecureChat.Desktop.Services.Calls;

public interface ISFrameTransform
{
    byte[] ProtectOutboundFrame(string callId, VideoFormat format, ReadOnlySpan<byte> encodedFrame);
    byte[] UnprotectInboundFrame(string callId, VideoFormat format, ReadOnlySpan<byte> encodedFrame);
}

public sealed class NoOpSFrameTransform : ISFrameTransform
{
    public byte[] ProtectOutboundFrame(string callId, VideoFormat format, ReadOnlySpan<byte> encodedFrame)
    {
        return encodedFrame.ToArray();
    }

    public byte[] UnprotectInboundFrame(string callId, VideoFormat format, ReadOnlySpan<byte> encodedFrame)
    {
        return encodedFrame.ToArray();
    }
}
