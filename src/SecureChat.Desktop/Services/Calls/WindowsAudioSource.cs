using Concentus;
using Concentus.Enums;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SecureChat.Desktop.Models;
using SIPSorceryMedia.Abstractions;
using System.Threading;

namespace SecureChat.Desktop.Services.Calls;

public sealed class WindowsAudioSource : IAudioSource, IDisposable
{
    private const int Channels = 1;
    private const int SampleRate = 48000;
    private const int BitsPerSample = 16;
    private const int FrameMilliseconds = 20;
    private const int SamplesPerFrame = SampleRate * FrameMilliseconds / 1000;
    private const int BytesPerFrame = SamplesPerFrame * Channels * (BitsPerSample / 8);
    private const int MaxEncodedBytes = 4000;
    private const int OpusPayloadType = 111;

    private readonly object _syncRoot = new();
    private readonly object _encodeSyncRoot = new();
    private readonly List<byte> _microphoneBuffer = [];
    private readonly List<byte> _systemAudioBuffer = [];
    private readonly List<AudioFormat> _audioFormats = new()
    {
        new AudioFormat(AudioCodecsEnum.OPUS, OpusPayloadType, SampleRate, SampleRate, Channels, "minptime=20;useinbandfec=1")
    };
    private readonly IOpusEncoder _encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP, null);
    private readonly AudioInputDeviceOption? _microphoneDevice;
    private readonly AudioInputDeviceOption? _systemAudioDevice;

    private IWaveIn? _microphoneCaptureDevice;
    private IWaveIn? _systemAudioCaptureDevice;
    private WaveFormat? _microphoneWaveFormat;
    private WaveFormat? _systemAudioWaveFormat;
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;
    private AudioFormat _selectedFormat;
    private bool _disposed;
    private bool _isStarted;
    private bool _paused;

    public WindowsAudioSource(AudioInputDeviceOption? microphoneDevice = null, AudioInputDeviceOption? systemAudioDevice = null, int opusBitrate = 24_000)
    {
        _microphoneDevice = microphoneDevice;
        _systemAudioDevice = systemAudioDevice;
        _encoder.Bitrate = Math.Clamp(opusBitrate, 6_144, 128_000);
        _selectedFormat = _audioFormats[0];
    }

    public event EncodedSampleDelegate OnAudioSourceEncodedSample = delegate { };
    public event Action<EncodedAudioFrame> OnAudioSourceEncodedFrameReady = delegate { };
    public event RawAudioSampleDelegate OnAudioSourceRawSample = delegate { };
    public event SourceErrorDelegate OnAudioSourceError = delegate { };

    public Task PauseAudio()
    {
        _paused = true;
        return Task.CompletedTask;
    }

    public Task ResumeAudio()
    {
        _paused = false;
        return Task.CompletedTask;
    }

    public Task StartAudio()
    {
        ThrowIfDisposed();
        if (_isStarted)
        {
            return Task.CompletedTask;
        }

        if (_audioFormats.Count == 0)
        {
            throw new InvalidOperationException("未保留任何可用音频格式。请确认 OPUS 音频格式没有被过滤掉。");
        }

        if (_microphoneDevice is null && _systemAudioDevice is null)
        {
            throw new InvalidOperationException("未选择任何音频输入源。请至少选择一个麦克风或启用系统音频混入。");
        }

        if (_microphoneDevice is not null)
        {
            _microphoneCaptureDevice ??= CreateCaptureDevice(_microphoneDevice, isSystemLoopback: false);
            _microphoneCaptureDevice.StartRecording();
        }

        if (_systemAudioDevice is not null)
        {
            _systemAudioCaptureDevice ??= CreateCaptureDevice(_systemAudioDevice, isSystemLoopback: true);
            _systemAudioCaptureDevice.StartRecording();
        }

        _processingCts = new CancellationTokenSource();
        _processingTask = Task.Run(() => ProcessAudioLoopAsync(_processingCts.Token));

        _isStarted = true;
        return Task.CompletedTask;
    }

    public async Task CloseAudio()
    {
        if (!_isStarted)
        {
            return;
        }

        _microphoneCaptureDevice?.StopRecording();
        _systemAudioCaptureDevice?.StopRecording();
        _isStarted = false;

        lock (_syncRoot)
        {
            _microphoneBuffer.Clear();
            _systemAudioBuffer.Clear();
        }

        if (_processingCts is not null)
        {
            await _processingCts.CancelAsync();
            _processingCts.Dispose();
            _processingCts = null;
        }

        if (_processingTask is not null)
        {
            try
            {
                await _processingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _processingTask = null;
        }

        return;
    }

    public List<AudioFormat> GetAudioSourceFormats() => [.. _audioFormats];

    public void SetAudioSourceFormat(AudioFormat audioFormat)
    {
        _selectedFormat = audioFormat;
    }

    public void RestrictFormats(Func<AudioFormat, bool> filter)
    {
        _audioFormats.RemoveAll(format => !filter(format));
        if (_audioFormats.Count > 0 && !_audioFormats.Any(format => format.FormatID == _selectedFormat.FormatID))
        {
            _selectedFormat = _audioFormats[0];
        }
    }

    public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
    {
        EncodeAndPublishFrame(sample, durationMilliseconds);
    }

    public bool HasEncodedAudioSubscribers() => true;

    public bool IsAudioSourcePaused() => _paused;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _microphoneCaptureDevice?.Dispose();
        _systemAudioCaptureDevice?.Dispose();
    }

    private IWaveIn CreateCaptureDevice(AudioInputDeviceOption device, bool isSystemLoopback)
    {
        IWaveIn capture = isSystemLoopback
            ? CreateLoopbackCapture(device)
            : CreateMicrophoneCapture(device);

        if (isSystemLoopback)
        {
            _systemAudioWaveFormat = capture.WaveFormat;
            capture.DataAvailable += (_, args) => HandleDataAvailable(args, isSystemLoopback: true);
        }
        else
        {
            _microphoneWaveFormat = capture.WaveFormat;
            capture.DataAvailable += (_, args) => HandleDataAvailable(args, isSystemLoopback: false);
        }

        capture.RecordingStopped += (_, args) =>
        {
            if (args.Exception is not null)
            {
                OnAudioSourceError($"音频采集停止: {args.Exception.Message}");
            }
        };

        return capture;
    }

    private static WaveInEvent CreateMicrophoneCapture(AudioInputDeviceOption device)
    {
        return new WaveInEvent
        {
            BufferMilliseconds = FrameMilliseconds,
            DeviceNumber = device.DeviceNumber ?? 0,
            NumberOfBuffers = 3,
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels)
        };
    }

    private static IWaveIn CreateLoopbackCapture(AudioInputDeviceOption device)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice endpoint;
        if (!string.IsNullOrWhiteSpace(device.DeviceId))
        {
            endpoint = enumerator.GetDevice(device.DeviceId);
        }
        else
        {
            endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        return new WasapiLoopbackCapture(endpoint);
    }

    private void HandleDataAvailable(WaveInEventArgs e, bool isSystemLoopback)
    {
        if (_paused || e.BytesRecorded <= 0)
        {
            return;
        }

        var waveFormat = isSystemLoopback ? _systemAudioWaveFormat : _microphoneWaveFormat;
        if (waveFormat is null)
        {
            OnAudioSourceError("音频采集格式未初始化。");
            return;
        }

        var normalizedPcm = NormalizeToTargetPcm(e.Buffer, e.BytesRecorded, waveFormat);
        if (normalizedPcm.Length == 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            var targetBuffer = isSystemLoopback ? _systemAudioBuffer : _microphoneBuffer;
            targetBuffer.AddRange(normalizedPcm);
            TrimBufferedAudio(targetBuffer);
        }
    }

    private async Task ProcessAudioLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(FrameMilliseconds));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_paused)
            {
                continue;
            }

            short[]? mixedFrame = null;
            lock (_syncRoot)
            {
                if (_microphoneBuffer.Count == 0 && _systemAudioBuffer.Count == 0)
                {
                    continue;
                }

                var microphoneFrame = DequeueFrameOrSilence(_microphoneBuffer);
                var systemAudioFrame = DequeueFrameOrSilence(_systemAudioBuffer);
                mixedFrame = MixFrames(microphoneFrame, systemAudioFrame);
            }

            if (mixedFrame is not null)
            {
                EncodeAndPublishFrame(mixedFrame, FrameMilliseconds);
            }
        }
    }

    private static void TrimBufferedAudio(List<byte> buffer)
    {
        const int maxBufferedFrames = 10;
        var maxBufferedBytes = BytesPerFrame * maxBufferedFrames;
        if (buffer.Count <= maxBufferedBytes)
        {
            return;
        }

        var bytesToDrop = buffer.Count - maxBufferedBytes;
        bytesToDrop -= bytesToDrop % BytesPerFrame;
        if (bytesToDrop > 0)
        {
            buffer.RemoveRange(0, bytesToDrop);
        }
    }

    private static short[] DequeueFrameOrSilence(List<byte> buffer)
    {
        if (buffer.Count < BytesPerFrame)
        {
            return new short[SamplesPerFrame * Channels];
        }

        var frameBytes = buffer.GetRange(0, BytesPerFrame).ToArray();
        buffer.RemoveRange(0, BytesPerFrame);
        var pcm = new short[SamplesPerFrame * Channels];
        Buffer.BlockCopy(frameBytes, 0, pcm, 0, BytesPerFrame);
        return pcm;
    }

    private static short[] MixFrames(short[] microphoneFrame, short[] systemAudioFrame)
    {
        var length = Math.Max(microphoneFrame.Length, systemAudioFrame.Length);
        var mixed = new short[length];
        for (var index = 0; index < length; index++)
        {
            var microphoneSample = index < microphoneFrame.Length ? microphoneFrame[index] : 0;
            var systemSample = index < systemAudioFrame.Length ? systemAudioFrame[index] : 0;
            var value = microphoneSample + systemSample;
            mixed[index] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        }

        return mixed;
    }

    private byte[] NormalizeToTargetPcm(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        if (bytesRecorded <= 0)
        {
            return [];
        }

        if (sourceFormat.Encoding == WaveFormatEncoding.Pcm
            && sourceFormat.SampleRate == SampleRate
            && sourceFormat.BitsPerSample == BitsPerSample
            && sourceFormat.Channels == Channels)
        {
            return buffer.AsSpan(0, bytesRecorded).ToArray();
        }

        var monoSamples = ExtractMonoSamples(buffer, bytesRecorded, sourceFormat);
        if (monoSamples.Length == 0)
        {
            return [];
        }

        var targetSamples = sourceFormat.SampleRate == SampleRate
            ? monoSamples
            : ResampleLinear(monoSamples, sourceFormat.SampleRate, SampleRate);

        var pcmBytes = new byte[targetSamples.Length * sizeof(short)];
        Buffer.BlockCopy(targetSamples, 0, pcmBytes, 0, pcmBytes.Length);
        return pcmBytes;
    }

    private static short[] ExtractMonoSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var sourceSamples = new short[bytesRecorded / sizeof(short)];
            Buffer.BlockCopy(buffer, 0, sourceSamples, 0, bytesRecorded);
            var monoSamples = new short[sourceSamples.Length / channels];

            for (var frameIndex = 0; frameIndex < monoSamples.Length; frameIndex++)
            {
                var sum = 0;
                var offset = frameIndex * channels;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += sourceSamples[offset + channel];
                }

                monoSamples[frameIndex] = (short)(sum / channels);
            }

            return monoSamples;
        }

        if ((format.Encoding == WaveFormatEncoding.IeeeFloat || format.Encoding == WaveFormatEncoding.Extensible)
            && format.BitsPerSample == 32)
        {
            var floatSampleCount = bytesRecorded / sizeof(float);
            var floatSamples = new float[floatSampleCount];
            Buffer.BlockCopy(buffer, 0, floatSamples, 0, bytesRecorded);
            var monoSamples = new short[floatSampleCount / channels];

            for (var frameIndex = 0; frameIndex < monoSamples.Length; frameIndex++)
            {
                float sum = 0;
                var offset = frameIndex * channels;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += floatSamples[offset + channel];
                }

                var averaged = sum / channels;
                averaged = Math.Clamp(averaged, -1f, 1f);
                monoSamples[frameIndex] = (short)Math.Round(averaged * short.MaxValue);
            }

            return monoSamples;
        }

        throw new NotSupportedException($"当前音频采集格式暂未支持: {format.Encoding}, {format.SampleRate}Hz, {format.BitsPerSample}bit, {format.Channels}ch");
    }

    private static short[] ResampleLinear(short[] sourceSamples, int sourceRate, int targetRate)
    {
        if (sourceSamples.Length == 0 || sourceRate <= 0 || targetRate <= 0)
        {
            return [];
        }

        if (sourceRate == targetRate)
        {
            return sourceSamples;
        }

        var targetLength = Math.Max(1, (int)Math.Round(sourceSamples.Length * (double)targetRate / sourceRate));
        var resampled = new short[targetLength];

        for (var index = 0; index < targetLength; index++)
        {
            var sourcePosition = index * (double)sourceRate / targetRate;
            var leftIndex = Math.Min(sourceSamples.Length - 1, (int)Math.Floor(sourcePosition));
            var rightIndex = Math.Min(sourceSamples.Length - 1, leftIndex + 1);
            var fraction = sourcePosition - leftIndex;
            var sample = sourceSamples[leftIndex] + (sourceSamples[rightIndex] - sourceSamples[leftIndex]) * fraction;
            resampled[index] = (short)Math.Round(sample);
        }

        return resampled;
    }

    private void EncodeAndPublishFrame(short[] pcm, uint durationMilliseconds)
    {
        if (pcm.Length == 0)
        {
            return;
        }

        lock (_encodeSyncRoot)
        {
            var frameDurationMilliseconds = (uint)FrameMilliseconds;
            for (var offset = 0; offset < pcm.Length; offset += SamplesPerFrame)
            {
                var remainingSamples = pcm.Length - offset;
                var frame = new short[SamplesPerFrame];
                var copyLength = Math.Min(SamplesPerFrame, remainingSamples);
                Array.Copy(pcm, offset, frame, 0, copyLength);

                try
                {
                    OnAudioSourceRawSample(AudioSamplingRatesEnum.Rate48kHz, frameDurationMilliseconds, frame);
                }
                catch (Exception ex)
                {
                    OnAudioSourceError($"音频原始帧回调失败: {ex.Message}");
                }

                byte[] packet;
                try
                {
                    var encoded = new byte[MaxEncodedBytes];
                    var encodedLength = _encoder.Encode(frame, SamplesPerFrame, encoded, encoded.Length);
                    if (encodedLength <= 0)
                    {
                        continue;
                    }

                    packet = encoded.AsSpan(0, encodedLength).ToArray();
                }
                catch (Exception ex)
                {
                    OnAudioSourceError($"音频编码失败: {ex.Message}");
                    continue;
                }

                try
                {
                    OnAudioSourceEncodedSample(SamplesPerFrame, packet);
                    OnAudioSourceEncodedFrameReady(new EncodedAudioFrame(0, _selectedFormat, frameDurationMilliseconds, packet));
                }
                catch (Exception ex)
                {
                    OnAudioSourceError($"音频发送失败: {ex.Message}");
                }
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}