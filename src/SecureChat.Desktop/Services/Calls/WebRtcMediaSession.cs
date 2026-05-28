using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SecureChat.Desktop.Models;
using SecureChat.Shared.Contracts.Calls;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using System.Collections.Generic;
using System.Text;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using AvaloniaPixelFormat = Avalonia.Platform.PixelFormat;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPoint = System.Drawing.Point;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using Forms = System.Windows.Forms;
using System.Threading;

namespace SecureChat.Desktop.Services.Calls;

public sealed class WebRtcMediaSession : IAsyncDisposable
{
    private static readonly MediaSessionTuningOptions DefaultTuningOptions = new();
    private readonly Func<CallSignalRequest, CancellationToken, Task> _sendSignalAsync;
    private readonly Func<MediaSessionTuningOptions> _tuningOptionsProvider;
    private readonly Action<string> _statusCallback;
    private readonly Action<string> _stageCallback;
    private readonly Action<AvaloniaBitmap?> _localFrameCallback;
    private readonly Action<AvaloniaBitmap?> _remoteFrameCallback;
    private readonly ISFrameTransform _sframeTransform;
    private readonly List<RTCIceCandidateInit> _pendingRemoteCandidates = [];
    private readonly List<CallSignalMessageDto> _pendingSignals = [];
    private readonly SemaphoreSlim _sourceSwitchLock = new(1, 1);

    private RTCPeerConnection? _peerConnection;
    private IAudioSource? _localAudioSource;
    private IAudioSink? _remoteAudioSink;
    private IVideoSource? _localVideoSource;
    private FFmpegVideoEndPoint? _localPreviewVideoSink;
    private FFmpegVideoEndPoint? _remoteVideoSink;
    private AudioFormat _negotiatedAudioFormat;
    private VideoFormat _negotiatedVideoFormat;
    private string _callId = string.Empty;
    private string _localUserId = string.Empty;
    private VideoCaptureMode _currentCaptureMode;
    private string? _currentCameraDevicePath;
    private ScreenCaptureTargetOption? _currentScreenCaptureTarget;
    private bool _isDisposed;
    private bool _hasNegotiatedAudioFormat;
    private bool _hasNegotiatedVideoFormat;
    private bool _loggedFirstRemoteVideoRtp;
    private bool _loggedFirstRemoteEncodedFrame;
    private bool _loggedFirstRemoteDecodedFrame;
    private bool _loggedFirstLocalRawFrame;
    private bool _loggedFirstLocalEncodedFrame;
    private bool _loggedFirstLocalDecodedPreviewFrame;
    private bool _loggedLocalEncodedFrameDropBeforeNegotiation;
    private bool _restartedLocalVideoAfterNegotiation;
    private bool _requestedLocalKeyFrameAfterConnected;
    private bool _remoteVideoKeyFrameSeen;
    private bool _loggedRemoteVideoWaitingForKeyFrame;
    private bool _loggedRemoteVp8DescriptorStripped;
    private bool _usingScreenShareSource;
    private bool _localAudioStarted;
    private bool _localVideoStarted;
    private bool _stoppingLocalVideo;
    private MediaSessionMode _sessionMode = MediaSessionMode.SendReceive;
    private DrawingRectangle _screenPreviewBounds = new(0, 0, 1280, 720);
    private CancellationTokenSource? _screenPreviewCts;
    private Task? _screenPreviewTask;

    public WebRtcMediaSession(
        Func<CallSignalRequest, CancellationToken, Task> sendSignalAsync,
        Func<MediaSessionTuningOptions>? tuningOptionsProvider,
        Action<string> statusCallback,
        Action<string> stageCallback,
        Action<AvaloniaBitmap?> localFrameCallback,
        Action<AvaloniaBitmap?> remoteFrameCallback,
        ISFrameTransform? sframeTransform = null)
    {
        _sendSignalAsync = sendSignalAsync;
        _tuningOptionsProvider = tuningOptionsProvider ?? (() => DefaultTuningOptions);
        _statusCallback = statusCallback;
        _stageCallback = stageCallback;
        _localFrameCallback = localFrameCallback;
        _remoteFrameCallback = remoteFrameCallback;
        _sframeTransform = sframeTransform ?? new NoOpSFrameTransform();
    }

    public async Task InitializeAsync(
        string callId,
        string localUserId,
        bool isCaller,
        VideoCaptureMode captureMode,
        string? cameraDevicePath,
        ScreenCaptureTargetOption? screenCaptureTarget,
        AudioInputDeviceOption? microphoneDevice,
        AudioInputDeviceOption? systemAudioDevice,
        IReadOnlyList<IceServerDto> iceServers,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(
            callId,
            localUserId,
            isCaller,
            captureMode,
            cameraDevicePath,
            screenCaptureTarget,
            microphoneDevice,
            systemAudioDevice,
            iceServers,
            MediaSessionMode.SendReceive,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync(
        string callId,
        string localUserId,
        bool isCaller,
        VideoCaptureMode captureMode,
        string? cameraDevicePath,
        ScreenCaptureTargetOption? screenCaptureTarget,
        AudioInputDeviceOption? microphoneDevice,
        AudioInputDeviceOption? systemAudioDevice,
        IReadOnlyList<IceServerDto> iceServers,
        MediaSessionMode sessionMode = MediaSessionMode.SendReceive,
        CancellationToken cancellationToken = default)
    {
        if (_peerConnection is not null)
        {
            return;
        }

        _callId = callId;
        _localUserId = localUserId;
        _currentCaptureMode = captureMode;
        _currentCameraDevicePath = cameraDevicePath;
        _currentScreenCaptureTarget = screenCaptureTarget;
        _sessionMode = sessionMode;
        var tuningOptions = GetTuningOptions();
        FfmpegBootstrap.EnsureInitialized();
        _localAudioSource = sessionMode == MediaSessionMode.ViewOnly
            ? CreateSilentAudioSource()
            : CreateLocalAudioSource(microphoneDevice, systemAudioDevice, tuningOptions.AudioBitrate);
        _remoteAudioSink = sessionMode == MediaSessionMode.PublishOnly ? null : new WindowsAudioSink();
        if (_remoteAudioSink is not null)
        {
            _remoteAudioSink.OnAudioSinkError += HandleRemoteAudioError;
            _remoteAudioSink.RestrictFormats(format => format.Codec == AudioCodecsEnum.OPUS);
        }

        _usingScreenShareSource = sessionMode != MediaSessionMode.ViewOnly && captureMode == VideoCaptureMode.Screen;
        _localVideoSource = sessionMode == MediaSessionMode.ViewOnly
            ? CreatePassiveVideoSource(out var screenPreviewBounds)
            : CreateLocalSource(captureMode, cameraDevicePath, screenCaptureTarget, tuningOptions, out screenPreviewBounds);
        _screenPreviewBounds = screenPreviewBounds;
        SubscribeVideoSource(_localVideoSource);
        _stageCallback($"本地视频源类型: {_localVideoSource.GetType().Name}");
        _localPreviewVideoSink = ShouldUseEncodedLocalPreview(_localVideoSource)
            ? CreateLocalPreviewSink()
            : null;
        _remoteVideoSink = sessionMode == MediaSessionMode.PublishOnly ? null : new FFmpegVideoEndPoint(new Dictionary<string, string>());
        if (_remoteVideoSink is not null)
        {
            _remoteVideoSink.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);
            _remoteVideoSink.SetDecoderWrapper(FfmpegBootstrap.Vp8DecoderName);
            _remoteVideoSink.OnVideoSinkDecodedSample += HandleRemoteDecodedFrame;
            _remoteVideoSink.OnVideoSinkDecodedSampleFaster += HandleRemoteDecodedRawImage;
        }

        if (_remoteAudioSink is not null)
        {
            await _remoteAudioSink.StartAudioSink().ConfigureAwait(false);
        }

        if (_remoteVideoSink is not null)
        {
            await _remoteVideoSink.StartVideoSink().ConfigureAwait(false);
        }

        if (_localPreviewVideoSink is not null)
        {
            await _localPreviewVideoSink.StartVideoSink().ConfigureAwait(false);
        }

        var rtcConfiguration = new RTCConfiguration
        {
            iceServers = BuildIceServers(iceServers),
            iceTransportPolicy = RTCIceTransportPolicy.all,
            X_UseRtpFeedbackProfile = true
        };

        var peerConnection = new RTCPeerConnection(rtcConfiguration);
        _peerConnection = peerConnection;

        var audioTrack = new MediaStreamTrack(_localAudioSource.GetAudioSourceFormats(), GetTrackStatus(sessionMode));
        peerConnection.addTrack(audioTrack);
        var videoTrack = new MediaStreamTrack(_localVideoSource.GetVideoSourceFormats(), GetTrackStatus(sessionMode));
        peerConnection.addTrack(videoTrack);
        peerConnection.OnAudioFormatsNegotiated += formats =>
        {
            if (formats.Count == 0)
            {
                return;
            }

            var selectedFormat = formats[0];
            _negotiatedAudioFormat = selectedFormat;
            _hasNegotiatedAudioFormat = true;
            _remoteAudioSink?.SetAudioSinkFormat(selectedFormat);
            _localAudioSource.SetAudioSourceFormat(selectedFormat);
            _stageCallback($"音频格式已协商: {selectedFormat.FormatName}");
        };
        peerConnection.OnVideoFormatsNegotiated += formats =>
        {
            if (formats.Count == 0)
            {
                return;
            }

            var selectedFormat = formats[0];
            _negotiatedVideoFormat = selectedFormat;
            _hasNegotiatedVideoFormat = true;
            _remoteVideoSink?.SetVideoSinkFormat(selectedFormat);
            _localPreviewVideoSink?.SetVideoSinkFormat(selectedFormat);
            _localVideoSource.SetVideoSourceFormat(selectedFormat);
            _stageCallback($"媒体格式已协商: {selectedFormat.FormatName}");
            _ = RecreateLocalVideoSourceAfterNegotiationIfNeededAsync();
            ScheduleDelayedLocalKeyFrameRequest("视频格式已协商");
        };
        peerConnection.OnRtpPacketReceived += HandleIncomingRtpPacket;
        peerConnection.OnVideoFrameReceived += HandleRemoteEncodedFrame;
        peerConnection.onicecandidate += candidate =>
        {
            if (candidate is not null)
            {
                _ = SendIceCandidateAsync(candidate, CancellationToken.None);
            }
        };
        peerConnection.oniceconnectionstatechange += state => _statusCallback($"ICE 状态: {state}");
        peerConnection.onconnectionstatechange += async state =>
        {
            _statusCallback($"PeerConnection 状态: {state}");
            _stageCallback($"PeerConnection -> {state}");

            if (state == RTCPeerConnectionState.connected)
            {
                await EnsureLocalAudioStartedAsync().ConfigureAwait(false);
                await EnsureLocalVideoStartedAsync().ConfigureAwait(false);
                RequestLocalKeyFrameAfterConnected();
            }
            else if (state is RTCPeerConnectionState.closed or RTCPeerConnectionState.failed or RTCPeerConnectionState.disconnected)
            {
                await StopLocalAudioAsync().ConfigureAwait(false);
                await StopLocalVideoAsync().ConfigureAwait(false);
            }
        };

        _stageCallback($"本地视频输入: {(captureMode == VideoCaptureMode.Screen ? "屏幕采集" : "摄像头采集")}");
        _stageCallback("SFrame 挂点: outbound protect / inbound unprotect 已预留");
        if (iceServers.Count == 0)
        {
            _stageCallback("当前未配置 STUN/TURN 服务器；同一局域网通常可连接，跨公网设备可能在 ICE 阶段失败。");
        }

        await EnsureLocalAudioStartedAsync().ConfigureAwait(false);
        await EnsureLocalVideoStartedAsync().ConfigureAwait(false);
        await ApplyQueuedSignalsAsync(cancellationToken).ConfigureAwait(false);

        if (isCaller)
        {
            await CreateAndSendOfferAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SwitchVideoInputAsync(
        VideoCaptureMode captureMode,
        string? cameraDevicePath,
        ScreenCaptureTargetOption? screenCaptureTarget,
        CancellationToken cancellationToken = default)
    {
        if (_sessionMode == MediaSessionMode.ViewOnly)
        {
            return;
        }

        await _sourceSwitchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tuningOptions = GetTuningOptions();
            _currentCaptureMode = captureMode;
            _currentCameraDevicePath = cameraDevicePath;
            _currentScreenCaptureTarget = screenCaptureTarget;
            var previousSource = _localVideoSource;
            var wasStarted = _localVideoStarted;

            await StopLocalVideoAsync().ConfigureAwait(false);
            UnsubscribeVideoSource(previousSource);

            _usingScreenShareSource = captureMode == VideoCaptureMode.Screen;
            _localVideoSource = CreateLocalSource(captureMode, cameraDevicePath, screenCaptureTarget, tuningOptions, out var screenPreviewBounds);
            _screenPreviewBounds = screenPreviewBounds;
            SubscribeVideoSource(_localVideoSource);

            if (_hasNegotiatedVideoFormat)
            {
                _localVideoSource.SetVideoSourceFormat(_negotiatedVideoFormat);
            }

            _loggedFirstLocalRawFrame = false;
            _loggedFirstLocalDecodedPreviewFrame = false;
            _restartedLocalVideoAfterNegotiation = false;
            _requestedLocalKeyFrameAfterConnected = false;
            _localFrameCallback(null);
            DisposeLocalPreviewSink();
            _localPreviewVideoSink = ShouldUseEncodedLocalPreview(_localVideoSource)
                ? CreateLocalPreviewSink()
                : null;

            if (_localPreviewVideoSink is not null)
            {
                await _localPreviewVideoSink.StartVideoSink().ConfigureAwait(false);
            }

            await DisposeVideoSourceAsync(previousSource).ConfigureAwait(false);

            if (wasStarted || _peerConnection is not null)
            {
                await EnsureLocalVideoStartedAsync().ConfigureAwait(false);
                RequestLocalKeyFrameAfterConnected();
            }

            _stageCallback($"本地视频输入已热切换: {(captureMode == VideoCaptureMode.Screen ? "屏幕共享" : "摄像头采集")}");
        }
        finally
        {
            _sourceSwitchLock.Release();
        }
    }

    public async Task SwitchAudioInputAsync(
        AudioInputDeviceOption? microphoneDevice,
        AudioInputDeviceOption? systemAudioDevice,
        CancellationToken cancellationToken = default)
    {
        if (_sessionMode == MediaSessionMode.ViewOnly)
        {
            return;
        }

        await _sourceSwitchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tuningOptions = GetTuningOptions();
            var previousSource = _localAudioSource;
            var wasStarted = _localAudioStarted;

            await StopLocalAudioAsync().ConfigureAwait(false);
            UnsubscribeAudioSource(previousSource);

            _localAudioSource = CreateLocalAudioSource(microphoneDevice, systemAudioDevice, tuningOptions.AudioBitrate);
            if (_hasNegotiatedAudioFormat)
            {
                _localAudioSource.SetAudioSourceFormat(_negotiatedAudioFormat);
            }

            await DisposeAudioSourceAsync(previousSource).ConfigureAwait(false);

            if (wasStarted || _peerConnection is not null)
            {
                await EnsureLocalAudioStartedAsync().ConfigureAwait(false);
            }

            var audioMode = systemAudioDevice is null
                ? "麦克风"
                : microphoneDevice is null
                    ? "系统音频"
                    : "麦克风 + 系统音频混入";
            _stageCallback($"本地音频输入已热切换: {audioMode}");
        }
        finally
        {
            _sourceSwitchLock.Release();
        }
    }

    public async Task HandleSignalAsync(CallSignalMessageDto signal, CancellationToken cancellationToken = default)
    {
        if (string.Equals(signal.SenderUserId, _localUserId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_peerConnection is null)
        {
            _pendingSignals.Add(signal);
            _stageCallback($"信令已暂存，等待媒体会话就绪: {signal.SignalType}");
            return;
        }

        switch (signal.SignalType)
        {
            case "hangup":
                _stageCallback("收到远端挂断信令。 ");
                _statusCallback("对方已挂断通话。 ");
                break;
            case "sdp-offer":
                try
                {
                    await HandleOfferAsync(signal.PayloadJson, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _stageCallback($"处理 Offer 失败: {ex.Message}");
                    _statusCallback($"处理 Offer 失败: {ex.Message}");
                }
                break;
            case "sdp-answer":
                try
                {
                    await HandleAnswerAsync(signal.PayloadJson).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _stageCallback($"处理 Answer 失败: {ex.Message}");
                    _statusCallback($"处理 Answer 失败: {ex.Message}");
                }
                break;
            case "ice-candidate":
                HandleRemoteIceCandidate(signal.PayloadJson);
                break;
        }
    }

    private async Task ApplyQueuedSignalsAsync(CancellationToken cancellationToken)
    {
        if (_pendingSignals.Count == 0)
        {
            return;
        }

        var pendingSignals = _pendingSignals.ToArray();
        _pendingSignals.Clear();
        _stageCallback($"开始处理 {pendingSignals.Length} 条暂存信令。");

        foreach (var signal in pendingSignals.OrderBy(item => item.CreatedAtUtc))
        {
            await HandleSignalAsync(signal, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await StopLocalAudioAsync().ConfigureAwait(false);
        await StopLocalVideoAsync().ConfigureAwait(false);

        if (_peerConnection is not null)
        {
            try
            {
                _peerConnection.Close("Session disposed.");
            }
            catch
            {
                // Ignore shutdown failures.
            }

            _peerConnection.Dispose();
            _peerConnection = null;
        }

        _remoteVideoSink?.Dispose();
        _remoteVideoSink = null;
        DisposeLocalPreviewSink();

        if (_remoteAudioSink is IDisposable audioSinkDisposable)
        {
            audioSinkDisposable.Dispose();
        }

        _remoteAudioSink = null;

        UnsubscribeAudioSource(_localAudioSource);
        await DisposeAudioSourceAsync(_localAudioSource).ConfigureAwait(false);

        _localAudioSource = null;

        UnsubscribeVideoSource(_localVideoSource);
        await DisposeVideoSourceAsync(_localVideoSource).ConfigureAwait(false);

        _localVideoSource = null;
        _negotiatedAudioFormat = default!;
        _negotiatedVideoFormat = default;
        _hasNegotiatedAudioFormat = false;
        _hasNegotiatedVideoFormat = false;
        _restartedLocalVideoAfterNegotiation = false;
        _currentCaptureMode = default;
        _currentCameraDevicePath = null;
        _currentScreenCaptureTarget = null;
        _localFrameCallback(null);
        _remoteFrameCallback(null);
    }

    private static List<RTCIceServer> BuildIceServers(IReadOnlyList<IceServerDto> iceServers)
    {
        return iceServers
            .Where(server => server.Urls.Count > 0)
            .Select(server => new RTCIceServer
            {
                urls = string.Join(',', server.Urls),
                username = string.IsNullOrWhiteSpace(server.Username) ? null : server.Username,
                credential = string.IsNullOrWhiteSpace(server.Credential) ? null : server.Credential,
                credentialType = RTCIceCredentialType.password
            })
            .ToList();
    }

    private static MediaStreamStatusEnum GetTrackStatus(MediaSessionMode sessionMode)
    {
        return sessionMode switch
        {
            MediaSessionMode.PublishOnly => MediaStreamStatusEnum.SendOnly,
            MediaSessionMode.ViewOnly => MediaStreamStatusEnum.RecvOnly,
            _ => MediaStreamStatusEnum.SendRecv
        };
    }

    private static IVideoSource CreatePassiveVideoSource(out DrawingRectangle screenPreviewBounds)
    {
        screenPreviewBounds = new DrawingRectangle(0, 0, 1280, 720);
        return new PassiveVideoSource();
    }

    private static IAudioSource CreateSilentAudioSource()
    {
        return new SilentAudioSource();
    }

    private IAudioSource CreateLocalAudioSource(AudioInputDeviceOption? microphoneDevice, AudioInputDeviceOption? systemAudioDevice, int audioBitrate)
    {
        var source = new WindowsAudioSource(microphoneDevice, systemAudioDevice, audioBitrate);
        source.OnAudioSourceError += HandleLocalAudioError;
        source.RestrictFormats(format => format.Codec == AudioCodecsEnum.OPUS);
        source.OnAudioSourceEncodedSample += HandleEncodedLocalAudioSample;
        return source;
    }

    private void SubscribeVideoSource(IVideoSource source)
    {
        source.OnVideoSourceError += HandleLocalVideoError;
        source.OnVideoSourceRawSample += HandleLocalRawFrame;
        source.OnVideoSourceEncodedSample += HandleEncodedLocalFrame;
    }

    private void UnsubscribeVideoSource(IVideoSource? source)
    {
        if (source is null)
        {
            return;
        }

        source.OnVideoSourceError -= HandleLocalVideoError;
        source.OnVideoSourceRawSample -= HandleLocalRawFrame;
        source.OnVideoSourceEncodedSample -= HandleEncodedLocalFrame;
    }

    private void UnsubscribeAudioSource(IAudioSource? source)
    {
        if (source is null)
        {
            return;
        }

        source.OnAudioSourceError -= HandleLocalAudioError;
        source.OnAudioSourceEncodedSample -= HandleEncodedLocalAudioSample;
    }

    private static async Task DisposeVideoSourceAsync(IVideoSource? source)
    {
        if (source is null)
        {
            return;
        }

        if (source is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (source is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static Task DisposeAudioSourceAsync(IAudioSource? source)
    {
        if (source is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return Task.CompletedTask;
    }

    private static IVideoSource CreateLocalSource(
        VideoCaptureMode captureMode,
        string? cameraDevicePath,
        ScreenCaptureTargetOption? screenCaptureTarget,
        MediaSessionTuningOptions tuningOptions,
        out DrawingRectangle screenPreviewBounds)
    {
        if (captureMode == VideoCaptureMode.Screen)
        {
            return CreateScreenSource(screenCaptureTarget, tuningOptions, out screenPreviewBounds);
        }

        screenPreviewBounds = new DrawingRectangle(0, 0, 1280, 720);
        return CreateCameraSource(cameraDevicePath, tuningOptions);
    }

    private static IVideoSource CreateScreenSource(ScreenCaptureTargetOption? screenCaptureTarget, MediaSessionTuningOptions tuningOptions, out DrawingRectangle previewBounds)
    {
        var selectedTarget = screenCaptureTarget
            ?? new ScreenCaptureTargetOption(
                "整个桌面",
                "desktop",
                Forms.Screen.PrimaryScreen?.Bounds ?? new DrawingRectangle(0, 0, 1280, 720),
                IsDesktop: true);
        var bounds = selectedTarget.Bounds;
        previewBounds = bounds;
        var source = new FFmpegScreenSource(selectedTarget.SourcePath, bounds, 8);
        source.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);
        if (!source.SetEncoderForCodec(VideoCodecsEnum.VP8, FfmpegBootstrap.Vp8EncoderName, new Dictionary<string, string>()))
        {
            source.Dispose();
            throw new InvalidOperationException($"无法初始化屏幕共享 VP8 编码器。请确认 FFmpeg 共享库可用。当前目标: {selectedTarget.DisplayName}");
        }

        var formats = source.GetVideoSourceFormats();
        if (formats.Count > 0)
        {
            source.SetVideoSourceFormat(formats[0]);
        }

        source.SetVideoEncoderBitrate(tuningOptions.ScreenShareBitrate, tuningOptions.ScreenShareFrameRate, null, null);
        return source;
    }

    private static IVideoSource CreateCameraSource(string? cameraDevicePath, MediaSessionTuningOptions tuningOptions)
    {
        if (string.IsNullOrWhiteSpace(cameraDevicePath))
        {
            throw new InvalidOperationException("未选择可用摄像头。请先刷新设备列表并选择摄像头，或切换到屏幕共享。");
        }

        try
        {
            var source = new FFmpegCameraSource(cameraDevicePath);
            source.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);
            if (!source.SetEncoderForCodec(VideoCodecsEnum.VP8, FfmpegBootstrap.Vp8EncoderName, new Dictionary<string, string>()))
            {
                source.Dispose();
                throw new InvalidOperationException("无法初始化摄像头 VP8 编码器。请确认 FFmpeg 共享库可用。");
            }

            source.SetVideoEncoderBitrate(tuningOptions.CameraVideoBitrate, tuningOptions.CameraFrameRate, null, null);
            return source;
        }
        catch
        {
            var cameraIndex = ResolveCameraIndex(cameraDevicePath);
            if (cameraIndex is not null)
            {
                return new OpenCvCameraVideoSource(cameraIndex.Value, 1280, 720, tuningOptions.CameraFrameRate);
            }

            throw;
        }
    }

    private static int? ResolveCameraIndex(string cameraDevicePath)
    {
        FfmpegBootstrap.EnsureInitialized();
        var devices = FFmpegCameraManager.GetCameraDevices() ?? [];
        var orderedDevices = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Path))
            .GroupBy(device => device.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(device => string.IsNullOrWhiteSpace(device.Name) ? device.Path : device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (var index = 0; index < orderedDevices.Count; index++)
        {
            if (string.Equals(orderedDevices[index].Path, cameraDevicePath, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return null;
    }

    private MediaSessionTuningOptions GetTuningOptions()
    {
        return _tuningOptionsProvider();
    }

    private async Task CreateAndSendOfferAsync(CancellationToken cancellationToken)
    {
        var peerConnection = _peerConnection ?? throw new InvalidOperationException("Peer connection has not been initialised.");
        var offer = peerConnection.createOffer(null);
        offer.sdp = FilterAudioSdpToOpusOnly(offer.sdp);
        _stageCallback($"Offer 音频行: {ExtractAudioMediaLine(offer.sdp)}");
        await peerConnection.setLocalDescription(offer).ConfigureAwait(false);
        await _sendSignalAsync(new CallSignalRequest
        {
            CallId = _callId,
            SenderUserId = _localUserId,
            SignalType = "sdp-offer",
            PayloadJson = offer.toJSON()
        }, cancellationToken).ConfigureAwait(false);

        _stageCallback("本地 Offer 已发送。");
    }

    private async Task HandleOfferAsync(string payloadJson, CancellationToken cancellationToken)
    {
        if (_peerConnection is null)
        {
            return;
        }

        if (!RTCSessionDescriptionInit.TryParse(payloadJson, out var offerInit))
        {
            _stageCallback("忽略无法解析的 Offer。" );
            return;
        }

        offerInit.sdp = FilterAudioSdpToOpusOnly(offerInit.sdp);
        _stageCallback($"收到 Offer 音频行: {ExtractAudioMediaLine(offerInit.sdp)}");

        var result = _peerConnection.setRemoteDescription(offerInit);
        if (result != SetDescriptionResultEnum.OK)
        {
            throw new InvalidOperationException($"设置远端 Offer 失败: {result}");
        }

        ApplyQueuedIceCandidates();
        var answer = _peerConnection.createAnswer(null);
        answer.sdp = FilterAudioSdpToOpusOnly(answer.sdp);
        _stageCallback($"Answer 音频行: {ExtractAudioMediaLine(answer.sdp)}");
        await _peerConnection.setLocalDescription(answer).ConfigureAwait(false);
        await _sendSignalAsync(new CallSignalRequest
        {
            CallId = _callId,
            SenderUserId = _localUserId,
            SignalType = "sdp-answer",
            PayloadJson = answer.toJSON()
        }, cancellationToken).ConfigureAwait(false);

        _stageCallback("远端 Offer 已处理，本地 Answer 已发送。");
    }

    private Task HandleAnswerAsync(string payloadJson)
    {
        if (_peerConnection is null)
        {
            return Task.CompletedTask;
        }

        if (!RTCSessionDescriptionInit.TryParse(payloadJson, out var answerInit))
        {
            _stageCallback("忽略无法解析的 Answer。");
            return Task.CompletedTask;
        }

        answerInit.sdp = FilterAudioSdpToOpusOnly(answerInit.sdp);
        _stageCallback($"收到 Answer 音频行: {ExtractAudioMediaLine(answerInit.sdp)}");

        var result = _peerConnection.setRemoteDescription(answerInit);
        if (result != SetDescriptionResultEnum.OK)
        {
            throw new InvalidOperationException($"设置远端 Answer 失败: {result}");
        }

        ApplyQueuedIceCandidates();
        _stageCallback("远端 Answer 已应用。");
        return Task.CompletedTask;
    }

    private void HandleRemoteIceCandidate(string payloadJson)
    {
        if (!RTCIceCandidateInit.TryParse(payloadJson, out var candidateInit))
        {
            return;
        }

        if (_peerConnection?.remoteDescription is null)
        {
            _pendingRemoteCandidates.Add(candidateInit);
            return;
        }

        _peerConnection.addIceCandidate(candidateInit);
        _stageCallback($"已添加远端 ICE 候选: {candidateInit.candidate}");
    }

    private void ApplyQueuedIceCandidates()
    {
        if (_peerConnection?.remoteDescription is null || _pendingRemoteCandidates.Count == 0)
        {
            return;
        }

        foreach (var candidate in _pendingRemoteCandidates)
        {
            _peerConnection.addIceCandidate(candidate);
        }

        _stageCallback($"已补发 {_pendingRemoteCandidates.Count} 条积压的 ICE 候选。");
        _pendingRemoteCandidates.Clear();
    }

    private async Task SendIceCandidateAsync(RTCIceCandidate candidate, CancellationToken cancellationToken)
    {
        await _sendSignalAsync(new CallSignalRequest
        {
            CallId = _callId,
            SenderUserId = _localUserId,
            SignalType = "ice-candidate",
            PayloadJson = candidate.toJSON()
        }, cancellationToken).ConfigureAwait(false);
    }

    private void HandleEncodedLocalFrame(uint durationRtpUnits, byte[] encodedFrame)
    {
        var peerConnection = _peerConnection;
        if (!_loggedFirstLocalEncodedFrame)
        {
            _loggedFirstLocalEncodedFrame = true;
            _stageCallback($"已收到首个本地编码视频帧，长度={encodedFrame.Length}, duration={durationRtpUnits}");
        }

        if (peerConnection is null)
        {
            return;
        }

        if (!_hasNegotiatedVideoFormat)
        {
            if (!_loggedLocalEncodedFrameDropBeforeNegotiation)
            {
                _loggedLocalEncodedFrameDropBeforeNegotiation = true;
                _stageCallback("本地编码视频帧已产生，但视频格式尚未协商完成，暂不发送。等待 SDP 协商完成后会继续发送后续帧。");
            }

            return;
        }

        var protectedFrame = _sframeTransform.ProtectOutboundFrame(_callId, _negotiatedVideoFormat, encodedFrame);
        TryRenderLocalPreviewFromEncodedFrame(protectedFrame, _negotiatedVideoFormat);
        peerConnection.SendVideo(durationRtpUnits, protectedFrame);
    }

    private void TryRenderLocalPreviewFromEncodedFrame(byte[] encodedFrame, VideoFormat format)
    {
        if (_loggedFirstLocalRawFrame || _localPreviewVideoSink is null)
        {
            return;
        }

        try
        {
            _localPreviewVideoSink.GotVideoFrame(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0), 0, encodedFrame, format);
        }
        catch (Exception ex)
        {
            _stageCallback($"本地预览回退解码失败: {ex.Message}");
        }
    }

    private void HandleEncodedLocalAudioSample(uint durationRtpUnits, byte[] encodedSample)
    {
        var peerConnection = _peerConnection;
        if (peerConnection is null || !_hasNegotiatedAudioFormat)
        {
            return;
        }

        peerConnection.SendAudio(durationRtpUnits, encodedSample);
    }

    private void HandleLocalAudioError(string message)
    {
        _statusCallback($"本地音频错误: {message}");
        _stageCallback($"本地音频错误: {message}");
    }

    private void HandleRemoteAudioError(string message)
    {
        _statusCallback($"远端音频错误: {message}");
        _stageCallback($"远端音频错误: {message}");
    }

    private void HandleLocalVideoError(string message)
    {
        if ((_stoppingLocalVideo || _isDisposed) && IsExpectedVideoShutdownMessage(message))
        {
            return;
        }

        _statusCallback($"本地视频错误: {message}");
        _stageCallback($"本地视频错误: {message}");
    }

    private static bool IsExpectedVideoShutdownMessage(string message)
    {
        return message.Contains("End of file", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Codec CELB is not supported by this endpoint.", StringComparison.OrdinalIgnoreCase);
    }

    private void HandleIncomingRtpPacket(System.Net.IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket packet)
    {
        if (mediaType == SDPMediaTypesEnum.video && !_loggedFirstRemoteVideoRtp)
        {
            _loggedFirstRemoteVideoRtp = true;
            _stageCallback($"已收到首个远端视频 RTP 包，PayloadType={packet.Header.PayloadType}, Timestamp={packet.Header.Timestamp}");
        }

        if (mediaType == SDPMediaTypesEnum.video)
        {
            return;
        }

        if (mediaType != SDPMediaTypesEnum.audio || _remoteAudioSink is null)
        {
            return;
        }

    #pragma warning disable CS0618
        _remoteAudioSink.GotAudioRtp(
            remoteEndPoint,
            packet.Header.SyncSource,
            packet.Header.SequenceNumber,
            packet.Header.Timestamp,
            packet.Header.PayloadType,
            packet.Header.MarkerBit != 0,
            packet.Payload);
    #pragma warning restore CS0618
    }

    private void HandleRemoteEncodedFrame(System.Net.IPEndPoint remoteEndPoint, uint timestamp, byte[] encodedFrame, VideoFormat format)
    {
        if (_remoteVideoSink is null)
        {
            return;
        }

        if (!_loggedFirstRemoteEncodedFrame)
        {
            _loggedFirstRemoteEncodedFrame = true;
            _stageCallback($"已收到首个远端编码视频帧，格式={format.FormatName}, 长度={encodedFrame.Length}");
        }

        try
        {
            var clearFrame = _sframeTransform.UnprotectInboundFrame(_callId, format, encodedFrame);
            if (format.Codec == VideoCodecsEnum.VP8)
            {
                clearFrame = NormalizeRemoteVp8Frame(clearFrame);
                if (!AcceptRemoteVp8Frame(clearFrame))
                {
                    return;
                }
            }

            _remoteVideoSink.GotVideoFrame(remoteEndPoint, timestamp, clearFrame, format);
        }
        catch (Exception ex)
        {
            _stageCallback($"远端视频帧送入解码器失败: {ex.Message}");
            _statusCallback($"远端视频帧送入解码器失败: {ex.Message}");
        }
    }

    private byte[] NormalizeRemoteVp8Frame(byte[] encodedFrame)
    {
        if (IsVp8KeyFrame(encodedFrame))
        {
            return encodedFrame;
        }

        if (!TryGetVp8PayloadDescriptorLength(encodedFrame, out var descriptorLength) || descriptorLength <= 0 || descriptorLength >= encodedFrame.Length)
        {
            return encodedFrame;
        }

        var frame = encodedFrame[descriptorLength..];
        if (!_loggedRemoteVp8DescriptorStripped)
        {
            _loggedRemoteVp8DescriptorStripped = true;
            _stageCallback($"已剥离远端 VP8 RTP 载荷头，头长={descriptorLength}，视频帧长度={frame.Length}。");
        }

        return frame;
    }

    private void RequestLocalKeyFrameAfterConnected()
    {
        if (_requestedLocalKeyFrameAfterConnected || _localVideoSource is null)
        {
            return;
        }

        _requestedLocalKeyFrameAfterConnected = true;
        try
        {
            _localVideoSource.ForceKeyFrame();
            _stageCallback("连接已建立，已请求本地视频关键帧。");
        }
        catch (Exception ex)
        {
            _stageCallback($"请求本地视频关键帧失败: {ex.Message}");
        }
    }

    private void ScheduleDelayedLocalKeyFrameRequest(string reason)
    {
        if (_localVideoSource is null || _isDisposed)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (_isDisposed || _localVideoSource is null)
            {
                return;
            }

            try
            {
                _localVideoSource.ForceKeyFrame();
                _stageCallback($"{reason}，已补发一次本地视频关键帧请求。");
            }
            catch (Exception ex)
            {
                _stageCallback($"{reason}后的关键帧补发失败: {ex.Message}");
            }
        });
    }

    private bool AcceptRemoteVp8Frame(byte[] encodedFrame)
    {
        if (_remoteVideoKeyFrameSeen)
        {
            return true;
        }

        if (IsVp8KeyFrame(encodedFrame))
        {
            _remoteVideoKeyFrameSeen = true;
            _stageCallback("已收到首个远端 VP8 关键帧，开始送入解码器。");
            return true;
        }

        if (!_loggedRemoteVideoWaitingForKeyFrame)
        {
            _loggedRemoteVideoWaitingForKeyFrame = true;
            _stageCallback($"远端 VP8 首帧不是关键帧，等待关键帧后再解码。首字节=0x{(encodedFrame.Length > 0 ? encodedFrame[0] : 0):X2}");
        }

        return false;
    }

    private static bool IsVp8KeyFrame(byte[] encodedFrame)
    {
        return encodedFrame.Length >= 6
            && (encodedFrame[0] & 0x01) == 0
            && encodedFrame[3] == 0x9D
            && encodedFrame[4] == 0x01
            && encodedFrame[5] == 0x2A;
    }

    private static bool TryGetVp8PayloadDescriptorLength(byte[] payload, out int descriptorLength)
    {
        descriptorLength = 0;
        if (payload.Length < 2)
        {
            return false;
        }

        var firstByte = payload[0];
        var startOfPartition = (firstByte & 0x10) != 0;
        var partitionIndex = firstByte & 0x0F;
        if (!startOfPartition || partitionIndex != 0)
        {
            return false;
        }

        var index = 1;
        var hasExtension = (firstByte & 0x80) != 0;
        if (hasExtension)
        {
            if (index >= payload.Length)
            {
                return false;
            }

            var extensionByte = payload[index++];
            if ((extensionByte & 0x80) != 0)
            {
                if (index >= payload.Length)
                {
                    return false;
                }

                var pictureIdByte = payload[index++];
                if ((pictureIdByte & 0x80) != 0)
                {
                    if (index >= payload.Length)
                    {
                        return false;
                    }

                    index++;
                }
            }

            if ((extensionByte & 0x40) != 0)
            {
                if (index >= payload.Length)
                {
                    return false;
                }

                index++;
            }

            if ((extensionByte & 0x30) != 0)
            {
                if (index >= payload.Length)
                {
                    return false;
                }

                index++;
            }
        }

        descriptorLength = index;
        return descriptorLength < payload.Length;
    }

    private void HandleLocalRawFrame(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        if (!_loggedFirstLocalRawFrame)
        {
            _loggedFirstLocalRawFrame = true;
            _stageCallback($"已收到首个本地原始视频帧，像素格式={pixelFormat}, 尺寸={width}x{height}");
        }

        var bitmap = CreateBitmapFromDecodedSample(width, height, sample, 0, pixelFormat);
        if (bitmap is null)
        {
            _stageCallback($"本地视频像素格式暂未预览: {pixelFormat}");
            return;
        }

        _localFrameCallback(bitmap);
    }

    private static string FilterAudioSdpToOpusOnly(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
        {
            return sdp ?? string.Empty;
        }

        var normalized = sdp.Replace("\r\n", "\n", StringComparison.Ordinal);
        var sections = normalized.Split("\nm=", StringSplitOptions.None);
        if (sections.Length == 0)
        {
            return sdp;
        }

        var rebuiltSections = new List<string> { sections[0] };

        for (var index = 1; index < sections.Length; index++)
        {
            var section = "m=" + sections[index];
            rebuiltSections.Add(FilterMediaSectionToOpus(section));
        }

        return string.Join("\r\n", rebuiltSections.SelectMany(section => section.Split('\n')));
    }

    private static string FilterMediaSectionToOpus(string section)
    {
        var lines = section.Split('\n', StringSplitOptions.None);
        if (lines.Length == 0 || !lines[0].StartsWith("m=audio ", StringComparison.OrdinalIgnoreCase))
        {
            return section;
        }

        var opusPayloadTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (!line.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payloadPart = line[9..];
            var separatorIndex = payloadPart.IndexOf(' ');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var payloadType = payloadPart[..separatorIndex];
            var codecDescriptor = payloadPart[(separatorIndex + 1)..];
            if (codecDescriptor.StartsWith("opus/", StringComparison.OrdinalIgnoreCase))
            {
                opusPayloadTypes.Add(payloadType);
            }
        }

        if (opusPayloadTypes.Count == 0)
        {
            return section;
        }

        var firstLineParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (firstLineParts.Length < 4)
        {
            return section;
        }

        var rewritten = new List<string>
        {
            string.Join(' ', firstLineParts.Take(3).Concat(opusPayloadTypes))
        };

        foreach (var rawLine in lines.Skip(1))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("a=rtpmap:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("a=fmtp:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("a=rtcp-fb:", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex < 0)
                {
                    continue;
                }

                var payloadPart = line[(colonIndex + 1)..];
                var separatorIndex = payloadPart.IndexOfAny([' ', '\t']);
                var payloadType = separatorIndex < 0 ? payloadPart : payloadPart[..separatorIndex];
                if (!opusPayloadTypes.Contains(payloadType))
                {
                    continue;
                }
            }

            rewritten.Add(rawLine);
        }

        return string.Join('\n', rewritten);
    }

    private static string ExtractAudioMediaLine(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp))
        {
            return "<empty>";
        }

        foreach (var line in sdp.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (line.StartsWith("m=audio ", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return "<missing m=audio>";
    }

    private void HandleRemoteDecodedFrame(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
    {
        if (!_loggedFirstRemoteDecodedFrame)
        {
            _loggedFirstRemoteDecodedFrame = true;
            _stageCallback($"已完成首个远端视频解码帧，像素格式={pixelFormat}, 尺寸={width}x{height}");
        }

        var bitmap = CreateBitmapFromDecodedSample((int)width, (int)height, sample, stride, pixelFormat);
        if (bitmap is null)
        {
            _stageCallback($"远端视频像素格式暂未渲染: {pixelFormat}");
            return;
        }

        _remoteFrameCallback(bitmap);
    }

    private void HandleLocalPreviewDecodedFrame(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
    {
        if (_loggedFirstLocalRawFrame)
        {
            return;
        }

        if (!_loggedFirstLocalDecodedPreviewFrame)
        {
            _loggedFirstLocalDecodedPreviewFrame = true;
            _stageCallback($"已完成首个本地回退预览解码帧，像素格式={pixelFormat}, 尺寸={width}x{height}");
        }

        var bitmap = CreateBitmapFromDecodedSample((int)width, (int)height, sample, stride, pixelFormat);
        if (bitmap is null)
        {
            _stageCallback($"本地回退预览像素格式暂未渲染: {pixelFormat}");
            return;
        }

        _localFrameCallback(bitmap);
    }

    private void HandleLocalPreviewDecodedRawImage(RawImage rawImage)
    {
        if (_loggedFirstLocalRawFrame)
        {
            return;
        }

        if (rawImage.Sample == IntPtr.Zero || rawImage.Width <= 0 || rawImage.Height <= 0)
        {
            _stageCallback("本地回退预览快速解码帧为空，已忽略。");
            return;
        }

        var stride = rawImage.Stride == 0 ? GetPackedStride(rawImage.Width, rawImage.PixelFormat) : Math.Abs(rawImage.Stride);
        if (stride <= 0)
        {
            _stageCallback($"本地回退预览快速解码帧像素格式暂未渲染: {rawImage.PixelFormat}, stride={rawImage.Stride}");
            return;
        }

        var sample = new byte[stride * rawImage.Height];
        Marshal.Copy(rawImage.Sample, sample, 0, sample.Length);

        if (!_loggedFirstLocalDecodedPreviewFrame)
        {
            _loggedFirstLocalDecodedPreviewFrame = true;
            _stageCallback($"已完成首个本地回退预览快速解码帧，像素格式={rawImage.PixelFormat}, 尺寸={rawImage.Width}x{rawImage.Height}");
        }

        var bitmap = CreateBitmapFromDecodedSample(rawImage.Width, rawImage.Height, sample, stride, rawImage.PixelFormat);
        if (bitmap is null)
        {
            _stageCallback($"本地回退预览快速解码帧像素格式暂未渲染: {rawImage.PixelFormat}, stride={stride}");
            return;
        }

        _localFrameCallback(bitmap);
    }

    private FFmpegVideoEndPoint CreateLocalPreviewSink()
    {
        var sink = new FFmpegVideoEndPoint(new Dictionary<string, string>());
        sink.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);
        sink.SetDecoderWrapper(FfmpegBootstrap.Vp8DecoderName);
        sink.OnVideoSinkDecodedSample += HandleLocalPreviewDecodedFrame;
        sink.OnVideoSinkDecodedSampleFaster += HandleLocalPreviewDecodedRawImage;
        return sink;
    }

    private void DisposeLocalPreviewSink()
    {
        if (_localPreviewVideoSink is null)
        {
            return;
        }

        _localPreviewVideoSink.OnVideoSinkDecodedSample -= HandleLocalPreviewDecodedFrame;
        _localPreviewVideoSink.OnVideoSinkDecodedSampleFaster -= HandleLocalPreviewDecodedRawImage;
        _localPreviewVideoSink.Dispose();
        _localPreviewVideoSink = null;
    }

    private static bool ShouldUseEncodedLocalPreview(IVideoSource source)
    {
        return source is FFmpegCameraSource;
    }

    private async Task RecreateLocalVideoSourceAfterNegotiationIfNeededAsync()
    {
        if (_restartedLocalVideoAfterNegotiation
            || _usingScreenShareSource
            || _localVideoSource is not FFmpegCameraSource
            || !_localVideoStarted
            || _currentCaptureMode != VideoCaptureMode.Camera)
        {
            return;
        }

        try
        {
            if (_restartedLocalVideoAfterNegotiation
                || _usingScreenShareSource
                || _localVideoSource is not FFmpegCameraSource
                || !_localVideoStarted
                || _currentCaptureMode != VideoCaptureMode.Camera)
            {
                return;
            }

            _restartedLocalVideoAfterNegotiation = true;
            await SwitchVideoInputAsync(
                _currentCaptureMode,
                _currentCameraDevicePath,
                _currentScreenCaptureTarget).ConfigureAwait(false);
            _restartedLocalVideoAfterNegotiation = true;
            _stageCallback("视频格式协商后已自动重建本地摄像头源，以应用协商格式。");
            ScheduleDelayedLocalKeyFrameRequest("本地摄像头源自动重建完成");
        }
        catch
        {
            _restartedLocalVideoAfterNegotiation = false;
            throw;
        }
    }

    private void HandleRemoteDecodedRawImage(RawImage rawImage)
    {
        if (rawImage.Sample == IntPtr.Zero || rawImage.Width <= 0 || rawImage.Height <= 0)
        {
            _stageCallback("远端快速解码帧为空，已忽略。");
            return;
        }

        var stride = rawImage.Stride == 0 ? GetPackedStride(rawImage.Width, rawImage.PixelFormat) : Math.Abs(rawImage.Stride);
        if (stride <= 0)
        {
            _stageCallback($"远端快速解码帧像素格式暂未渲染: {rawImage.PixelFormat}, stride={rawImage.Stride}");
            return;
        }

        var sample = new byte[stride * rawImage.Height];
        Marshal.Copy(rawImage.Sample, sample, 0, sample.Length);

        if (!_loggedFirstRemoteDecodedFrame)
        {
            _loggedFirstRemoteDecodedFrame = true;
            _stageCallback($"已完成首个远端快速解码帧，像素格式={rawImage.PixelFormat}, 尺寸={rawImage.Width}x{rawImage.Height}");
        }

        var bitmap = CreateBitmapFromDecodedSample(rawImage.Width, rawImage.Height, sample, stride, rawImage.PixelFormat);
        if (bitmap is null)
        {
            _stageCallback($"远端快速解码帧像素格式暂未渲染: {rawImage.PixelFormat}, stride={stride}");
            return;
        }

        _remoteFrameCallback(bitmap);
    }

    private static int GetPackedStride(int width, VideoPixelFormatsEnum pixelFormat)
    {
        return pixelFormat switch
        {
            VideoPixelFormatsEnum.Bgr or VideoPixelFormatsEnum.Rgb => width * 3,
            VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba => width * 4,
            VideoPixelFormatsEnum.I420 => width,
            VideoPixelFormatsEnum.NV12 => width,
            _ => 0
        };
    }

    private async Task EnsureLocalVideoStartedAsync()
    {
        if (_localVideoStarted || _localVideoSource is null)
        {
            return;
        }

        await _localVideoSource.StartVideo().ConfigureAwait(false);
        if (_usingScreenShareSource)
        {
            StartScreenPreviewLoop();
        }

        _localVideoStarted = true;
        _stageCallback("本地视频采集已启动。");
    }

    private async Task StopLocalVideoAsync()
    {
        if (!_localVideoStarted || _localVideoSource is null)
        {
            return;
        }

        _stoppingLocalVideo = true;
        try
        {
            await _localVideoSource.CloseVideo().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _stageCallback($"本地视频停止异常: {ex.Message}");
            _statusCallback($"本地视频停止异常: {ex.Message}");
        }

        await StopScreenPreviewLoopAsync().ConfigureAwait(false);

        _stoppingLocalVideo = false;

        _localVideoStarted = false;
        _stageCallback("本地视频采集已停止。");
    }

    private void StartScreenPreviewLoop()
    {
        if (_screenPreviewTask is not null)
        {
            return;
        }

        _screenPreviewCts = new CancellationTokenSource();
        _screenPreviewTask = Task.Run(() => ScreenPreviewLoopAsync(_screenPreviewCts.Token));
    }

    private async Task StopScreenPreviewLoopAsync()
    {
        if (_screenPreviewCts is not null)
        {
            _screenPreviewCts.Cancel();
        }

        if (_screenPreviewTask is not null)
        {
            try
            {
                await _screenPreviewTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during shutdown.
            }
            catch (Exception ex)
            {
                _stageCallback($"屏幕预览停止异常: {ex.Message}");
            }
        }

        _screenPreviewTask = null;
        _screenPreviewCts?.Dispose();
        _screenPreviewCts = null;
    }

    private async Task ScreenPreviewLoopAsync(CancellationToken cancellationToken)
    {
        var bounds = _screenPreviewBounds;
        var previewDelay = TimeSpan.FromMilliseconds(125);

        while (!cancellationToken.IsCancellationRequested)
        {
            using var screenBitmap = new DrawingBitmap(bounds.Width, bounds.Height, DrawingPixelFormat.Format32bppArgb);
            using var graphics = DrawingGraphics.FromImage(screenBitmap);
            graphics.CopyFromScreen(bounds.Location, DrawingPoint.Empty, bounds.Size);

            var preview = CreateBitmapFromScreenBitmap(screenBitmap);
            _localFrameCallback(preview);

            await Task.Delay(previewDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static AvaloniaBitmap CreateBitmapFromScreenBitmap(DrawingBitmap bitmap)
    {
        var rect = new DrawingRectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);

        try
        {
            var rowLength = bitmap.Width * 4;
            var packed = new byte[rowLength * bitmap.Height];
            for (var row = 0; row < bitmap.Height; row++)
            {
                var sourcePtr = IntPtr.Add(data.Scan0, row * data.Stride);
                Marshal.Copy(sourcePtr, packed, row * rowLength, rowLength);
            }

            return CreateBitmapFromBgraBuffer(bitmap.Width, bitmap.Height, packed);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private async Task EnsureLocalAudioStartedAsync()
    {
        if (_localAudioStarted || _localAudioSource is null)
        {
            return;
        }

        await _localAudioSource.StartAudio().ConfigureAwait(false);
        _localAudioStarted = true;
        _stageCallback("本地音频采集已启动。");
    }

    private async Task StopLocalAudioAsync()
    {
        if (!_localAudioStarted || _localAudioSource is null)
        {
            return;
        }

        await _localAudioSource.CloseAudio().ConfigureAwait(false);
        _localAudioStarted = false;
        _stageCallback("本地音频采集已停止。");
    }

    private static Bitmap CreateBitmapFromBgr(int width, int height, byte[] bgr, int stride = 0)
    {
        stride = stride == 0 ? width * 3 : stride;
        var bgra = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * stride;
            var targetRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = sourceRow + (x * 3);
                var targetIndex = targetRow + (x * 4);
                bgra[targetIndex] = bgr[sourceIndex];
                bgra[targetIndex + 1] = bgr[sourceIndex + 1];
                bgra[targetIndex + 2] = bgr[sourceIndex + 2];
                bgra[targetIndex + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), AvaloniaPixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var framebuffer = bitmap.Lock();
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, framebuffer.Address, bgra.Length);
        return bitmap;
    }

    private static Bitmap? CreateBitmapFromDecodedSample(int width, int height, byte[] sample, int stride, VideoPixelFormatsEnum pixelFormat)
    {
        return pixelFormat switch
        {
            VideoPixelFormatsEnum.Bgr => CreateBitmapFromBgr(width, height, sample, stride),
            VideoPixelFormatsEnum.Rgb => CreateBitmapFromRgb(width, height, sample, stride),
            VideoPixelFormatsEnum.Bgra => CreateBitmapFromBgra(width, height, sample, stride),
            VideoPixelFormatsEnum.Rgba => CreateBitmapFromRgba(width, height, sample, stride),
            VideoPixelFormatsEnum.I420 => CreateBitmapFromI420(width, height, sample, stride),
            VideoPixelFormatsEnum.NV12 => CreateBitmapFromNv12(width, height, sample, stride),
            _ => null
        };
    }

    private static Bitmap CreateBitmapFromRgb(int width, int height, byte[] rgb, int stride = 0)
    {
        stride = stride == 0 ? width * 3 : stride;
        var bgra = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * stride;
            var targetRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = sourceRow + (x * 3);
                var targetIndex = targetRow + (x * 4);
                bgra[targetIndex] = rgb[sourceIndex + 2];
                bgra[targetIndex + 1] = rgb[sourceIndex + 1];
                bgra[targetIndex + 2] = rgb[sourceIndex];
                bgra[targetIndex + 3] = 255;
            }
        }

        return CreateBitmapFromBgraBuffer(width, height, bgra);
    }

    private static Bitmap CreateBitmapFromBgra(int width, int height, byte[] bgra, int stride = 0)
    {
        stride = stride == 0 ? width * 4 : stride;
        var packed = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(bgra, y * stride, packed, y * width * 4, width * 4);
        }

        return CreateBitmapFromBgraBuffer(width, height, packed);
    }

    private static Bitmap CreateBitmapFromRgba(int width, int height, byte[] rgba, int stride = 0)
    {
        stride = stride == 0 ? width * 4 : stride;
        var bgra = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var sourceRow = y * stride;
            var targetRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = sourceRow + (x * 4);
                var targetIndex = targetRow + (x * 4);
                bgra[targetIndex] = rgba[sourceIndex + 2];
                bgra[targetIndex + 1] = rgba[sourceIndex + 1];
                bgra[targetIndex + 2] = rgba[sourceIndex];
                bgra[targetIndex + 3] = rgba[sourceIndex + 3];
            }
        }

        return CreateBitmapFromBgraBuffer(width, height, bgra);
    }

    private static Bitmap CreateBitmapFromI420(int width, int height, byte[] sample, int stride = 0)
    {
        var yStride = stride == 0 ? width : stride;
        var uvStride = Math.Max(1, yStride / 2);
        var yPlaneLength = yStride * height;
        var chromaHeight = Math.Max(1, height / 2);
        var uPlaneOffset = yPlaneLength;
        var vPlaneOffset = uPlaneOffset + (uvStride * chromaHeight);
        var bgra = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var yIndex = (y * yStride) + x;
                var uvIndex = ((y / 2) * uvStride) + (x / 2);
                var yy = sample[yIndex];
                var uu = sample[uPlaneOffset + uvIndex];
                var vv = sample[vPlaneOffset + uvIndex];
                WriteYuvPixel(bgra, width, x, y, yy, uu, vv);
            }
        }

        return CreateBitmapFromBgraBuffer(width, height, bgra);
    }

    private static Bitmap CreateBitmapFromNv12(int width, int height, byte[] sample, int stride = 0)
    {
        var yStride = stride == 0 ? width : stride;
        var uvStride = yStride;
        var yPlaneLength = yStride * height;
        var bgra = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var yIndex = (y * yStride) + x;
                var uvRow = (y / 2) * uvStride;
                var uvColumn = (x / 2) * 2;
                var uvIndex = yPlaneLength + uvRow + uvColumn;
                var yy = sample[yIndex];
                var uu = sample[uvIndex];
                var vv = sample[uvIndex + 1];
                WriteYuvPixel(bgra, width, x, y, yy, uu, vv);
            }
        }

        return CreateBitmapFromBgraBuffer(width, height, bgra);
    }

    private static void WriteYuvPixel(byte[] bgra, int width, int x, int y, byte ySample, byte uSample, byte vSample)
    {
        var c = ySample - 16;
        var d = uSample - 128;
        var e = vSample - 128;

        var red = ClampToByte((298 * c + 409 * e + 128) >> 8);
        var green = ClampToByte((298 * c - 100 * d - 208 * e + 128) >> 8);
        var blue = ClampToByte((298 * c + 516 * d + 128) >> 8);

        var index = ((y * width) + x) * 4;
        bgra[index] = blue;
        bgra[index + 1] = green;
        bgra[index + 2] = red;
        bgra[index + 3] = 255;
    }

    private static byte ClampToByte(int value) => (byte)Math.Clamp(value, 0, 255);

    private static Bitmap CreateBitmapFromBgraBuffer(int width, int height, byte[] bgra)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), AvaloniaPixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var framebuffer = bitmap.Lock();
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, framebuffer.Address, bgra.Length);
        return bitmap;
    }
}
