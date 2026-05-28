using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SecureChat.ClientCore.Calls;
using SecureChat.ClientCore.Live;
using SecureChat.ClientCore.Services;
using SecureChat.Core.Crypto;
using SecureChat.Core.Keys;
using SecureChat.Core.Models;
using SecureChat.Desktop.Models;
using SecureChat.Desktop.Services.Calls;
using SecureChat.Shared.Contracts.Messages;
using SecureChat.Shared.Contracts.Online;
using SecureChat.Shared.Contracts.Calls;
using SecureChat.Shared.Contracts.Live;

namespace SecureChat.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly JsonSerializerOptions CallSignalJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiClient _apiClient;
    private readonly IdentityKeyService _identityKeyService;
    private readonly IdentityBootstrapService _identityBootstrapService;
    private readonly MessageCryptoService _messageCryptoService = new();
    private readonly MicrosoftOAuthLoopbackService _microsoftOAuthLoopbackService;
    private readonly IVideoCallService _videoCallService;
    private readonly ILiveBroadcastService _liveBroadcastService;
    private readonly VideoDeviceDiscoveryService _videoDeviceDiscoveryService;
    private readonly ScreenCaptureTargetDiscoveryService _screenCaptureTargetDiscoveryService;
    private readonly AudioInputDeviceDiscoveryService _audioInputDeviceDiscoveryService;
    private readonly DispatcherTimer _presenceTimer;
    private ClientWebSocket? _callSignalSocket;
    private CancellationTokenSource? _callSignalCts;
    private WebRtcMediaSession? _mediaSession;
    private ClientWebSocket? _liveSignalSocket;
    private CancellationTokenSource? _liveSignalCts;
    private WebRtcMediaSession? _liveMediaSession;
    private PendingCallDto? _pendingIncomingCall;
    private string? _activeCallId;
    private Bitmap? _localVideoFrame;
    private Bitmap? _remoteVideoFrame;
    private Bitmap? _liveLocalVideoFrame;
    private Bitmap? _liveRemoteVideoFrame;
    private MessageAttachmentPayload? _pendingAttachment;
    private bool _suppressActiveInputHotSwitch;

    [ObservableProperty]
    private string currentUserId = string.Empty;

    [ObservableProperty]
    private string pendingUserId = string.Empty;

    [ObservableProperty]
    private string recipientUserId = string.Empty;

    [ObservableProperty]
    private string messageText = string.Empty;

    [ObservableProperty]
    private string currentDisplayName = "未登录";

    [ObservableProperty]
    private string statusText = "等待登录";

    [ObservableProperty]
    private string footerText = "桌面端已切到 Avalonia 方向，WinForms 仍可并行保留。";

    [ObservableProperty]
    private string videoStatusText = "视频区已预留信令与媒体能力接口，后续可以直接接 WebRTC / TURN。";

    [ObservableProperty]
    private string selectedVideoInputMode = "摄像头";

    [ObservableProperty]
    private string incomingCallText = "暂无待接听来电。";

    [ObservableProperty]
    private string pendingAttachmentSummary = "未选择附件。";

    [ObservableProperty]
    private string cameraSelectionText = "正在检测摄像头设备...";

    [ObservableProperty]
    private string screenCaptureSelectionText = "正在检测可共享窗口...";

    [ObservableProperty]
    private string audioInputSelectionText = "正在检测音频输入设备...";

    [ObservableProperty]
    private bool mixSystemAudioEnabled;

    [ObservableProperty]
    private string activeCallPeerText = "当前未在通话中。";

    [ObservableProperty]
    private double videoViewportHeight = 220;

    [ObservableProperty]
    private bool localPreviewVisible = true;

    [ObservableProperty]
    private bool remoteVideoFullscreen;

    [ObservableProperty]
    private string liveRoomName = string.Empty;

    [ObservableProperty]
    private string liveRoomCodeInput = string.Empty;

    [ObservableProperty]
    private string liveRoomStatusText = "直播模块预留：创建房间后可输出视频，所有用户后续都可按房间号选择进入观看。";

    [ObservableProperty]
    private string activeLiveRoomText = "当前未加入直播间。";

    [ObservableProperty]
    private string liveRoomDirectoryStatusText = "正在加载公开直播间目录...";

    [ObservableProperty]
    private LiveRoomDto? selectedPublicLiveRoom;

    [ObservableProperty]
    private VideoCaptureDeviceOption? selectedCameraDevice;

    [ObservableProperty]
    private ScreenCaptureTargetOption? selectedScreenCaptureTarget;

    [ObservableProperty]
    private AudioInputDeviceOption? selectedMicrophoneDevice;

    [ObservableProperty]
    private AudioInputDeviceOption? selectedSystemAudioDevice;

    [ObservableProperty]
    private int selectedAudioBitrateKbps = 24;

    [ObservableProperty]
    private int selectedCameraBitrateKbps = 1500;

    [ObservableProperty]
    private int selectedCameraFrameRate = 15;

    [ObservableProperty]
    private int selectedScreenShareBitrateKbps = 1500;

    [ObservableProperty]
    private int selectedScreenShareFrameRate = 8;

    [ObservableProperty]
    private OnlineUserItem? selectedOnlineUser;

    private string? _activeLiveRoomId;
    private string _activeLiveRoomDisplayName = "未进入直播间";
    private string _activeLiveRoomHostUserId = "-";
    private int _activeLiveRoomViewerCount;
    private bool _isLiveRoomHost;

    public ObservableCollection<MessageListItem> Messages { get; } = [];
    public ObservableCollection<OnlineUserItem> OnlineUsers { get; } = [];
    public ObservableCollection<string> VideoStages { get; } = [];
    public ObservableCollection<string> LiveRoomStages { get; } = [];
    public ObservableCollection<LiveRoomDto> PublicLiveRooms { get; } = [];
    public ObservableCollection<string> VideoInputModes { get; } = ["摄像头", "屏幕共享"];
    public ObservableCollection<VideoCaptureDeviceOption> CameraDevices { get; } = [];
    public ObservableCollection<ScreenCaptureTargetOption> ScreenCaptureTargets { get; } = [];
    public ObservableCollection<AudioInputDeviceOption> MicrophoneDevices { get; } = [];
    public ObservableCollection<AudioInputDeviceOption> SystemAudioDevices { get; } = [];
    public ObservableCollection<int> AudioBitrateOptionsKbps { get; } = [24, 32, 48, 64, 96];
    public ObservableCollection<int> CameraBitrateOptionsKbps { get; } = [1500, 2500, 4000, 6000];
    public ObservableCollection<int> CameraFrameRateOptions { get; } = [15, 24, 30];
    public ObservableCollection<int> ScreenShareBitrateOptionsKbps { get; } = [1500, 3000, 5000, 8000];
    public ObservableCollection<int> ScreenShareFrameRateOptions { get; } = [8, 12, 15, 20];
    public IAsyncRelayCommand SignInCommand { get; }
    public IAsyncRelayCommand UpdateUserIdCommand { get; }
    public IAsyncRelayCommand RegisterPublicKeyCommand { get; }
    public IAsyncRelayCommand SendMessageCommand { get; }
    public IAsyncRelayCommand RefreshInboxCommand { get; }
    public IAsyncRelayCommand RefreshOnlineUsersCommand { get; }
    public IAsyncRelayCommand RefreshVideoDevicesCommand { get; }
    public IAsyncRelayCommand PrepareVideoCallCommand { get; }
    public IAsyncRelayCommand AnswerIncomingCallCommand { get; }
    public IAsyncRelayCommand HangupCallCommand { get; }
    public IAsyncRelayCommand CreateLiveRoomCommand { get; }
    public IAsyncRelayCommand JoinLiveRoomCommand { get; }
    public IAsyncRelayCommand JoinSelectedLiveRoomCommand { get; }
    public IAsyncRelayCommand LeaveLiveRoomCommand { get; }
    public IAsyncRelayCommand RefreshLiveRoomsCommand { get; }

    public bool IsCameraInputSelected => string.Equals(SelectedVideoInputMode, "摄像头", StringComparison.OrdinalIgnoreCase);
    public bool IsScreenInputSelected => string.Equals(SelectedVideoInputMode, "屏幕共享", StringComparison.OrdinalIgnoreCase);
    public bool HasPendingAttachment => _pendingAttachment is not null;
    public bool IsCallActive => _mediaSession is not null && !string.IsNullOrWhiteSpace(_activeCallId);
    public bool IsLiveRoomActive => !string.IsNullOrWhiteSpace(_activeLiveRoomId);
    public bool IsLiveRoomHost => IsLiveRoomActive && _isLiveRoomHost;
    public bool IsLivePublishing => _liveMediaSession is not null && IsLiveRoomHost;
    public bool ShowLiveHostLayout => IsLiveRoomHost;
    public bool ShowLiveAudienceLayout => !IsLiveRoomHost;
    public bool HasPublicLiveRooms => PublicLiveRooms.Count > 0;
    public Bitmap? LiveRoomVideoFrame => IsLiveRoomHost ? _liveLocalVideoFrame : _liveRemoteVideoFrame;
    public string ActiveLiveRoomDisplayName => _activeLiveRoomDisplayName;
    public string ActiveLiveRoomHostText => $"UP主: {_activeLiveRoomHostUserId}";
    public string ActiveLiveRoomViewerCountText => $"{_activeLiveRoomViewerCount} 人观看";
    public string MediaQualitySelectionText =>
        $"音频 {SelectedAudioBitrateKbps} kbps | 摄像头 {SelectedCameraBitrateKbps} kbps / {SelectedCameraFrameRate} fps | 屏幕共享 {SelectedScreenShareBitrateKbps} kbps / {SelectedScreenShareFrameRate} fps";
    public string LiveRoomRoleBadgeText => !IsLiveRoomActive ? "未入场" : IsLiveRoomHost ? "主播" : "观众";
    public string LiveRoomRoleSummaryText => !IsLiveRoomActive
        ? "创建房间即可开播，或从右侧目录一键进入观看。"
        : IsLiveRoomHost
            ? "你正在推流，观众加入后会直接建立观看链路。"
            : "你正在观看直播，可随时切换回通话或消息窗口。";
    public string LiveRoomHeroHintText => IsLiveRoomHost
        ? "正在输出你的直播画面，本地设备切换会实时作用到推流。"
        : "正在播放主播画面，后续可继续补充弹幕、礼物和互动区。";
    public string LiveRoomPrimaryActionText => IsLiveRoomHost ? "正在直播中" : "创建公开直播房间";

    public Bitmap? LocalVideoFrame
    {
        get => _localVideoFrame;
        private set => SetBitmapProperty(ref _localVideoFrame, value);
    }

    public Bitmap? RemoteVideoFrame
    {
        get => _remoteVideoFrame;
        private set => SetBitmapProperty(ref _remoteVideoFrame, value);
    }

    public string LocalPreviewToggleText => LocalPreviewVisible ? "隐藏本地视角" : "显示本地视角";

    public string RemoteVideoFullscreenButtonText => RemoteVideoFullscreen ? "退出全屏" : "对方视角全屏";

    public MainWindowViewModel(Uri apiBaseUri)
    {
        _apiClient = new ApiClient(new HttpClient { BaseAddress = apiBaseUri });
        IKeyStore keyStore = OperatingSystem.IsWindows()
            ? new WindowsDpapiKeyStore()
            : new FileKeyStore();
        _identityKeyService = new IdentityKeyService(keyStore);
        _identityBootstrapService = new IdentityBootstrapService(_identityKeyService);
        _microsoftOAuthLoopbackService = new MicrosoftOAuthLoopbackService(_apiClient);
        _videoCallService = new PlaceholderVideoCallService();
        _liveBroadcastService = new ApiLiveBroadcastService(_apiClient);
        _videoDeviceDiscoveryService = new VideoDeviceDiscoveryService();
        _screenCaptureTargetDiscoveryService = new ScreenCaptureTargetDiscoveryService();
        _audioInputDeviceDiscoveryService = new AudioInputDeviceDiscoveryService();
        _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _presenceTimer.Tick += async (_, _) => await SendPresenceHeartbeatAsync();
        SignInCommand = new AsyncRelayCommand(SignInAsync);
        UpdateUserIdCommand = new AsyncRelayCommand(UpdateUserIdAsync);
        RegisterPublicKeyCommand = new AsyncRelayCommand(RegisterPublicKeyAsync);
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync);
        RefreshInboxCommand = new AsyncRelayCommand(RefreshInboxAsync);
        RefreshOnlineUsersCommand = new AsyncRelayCommand(RefreshOnlineUsersAsync);
        RefreshVideoDevicesCommand = new AsyncRelayCommand(RefreshVideoDevicesAsync);
        PrepareVideoCallCommand = new AsyncRelayCommand(PrepareVideoCallAsync);
        AnswerIncomingCallCommand = new AsyncRelayCommand(AnswerIncomingCallAsync);
        HangupCallCommand = new AsyncRelayCommand(HangupCallAsync);
        CreateLiveRoomCommand = new AsyncRelayCommand(CreateLiveRoomAsync);
        JoinLiveRoomCommand = new AsyncRelayCommand(JoinLiveRoomAsync);
        JoinSelectedLiveRoomCommand = new AsyncRelayCommand(JoinSelectedLiveRoomAsync);
        LeaveLiveRoomCommand = new AsyncRelayCommand(LeaveLiveRoomAsync);
        RefreshLiveRoomsCommand = new AsyncRelayCommand(RefreshLiveRoomsAsync);
        FooterText = $"API: {apiBaseUri}";
    }

    partial void OnSelectedVideoInputModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsCameraInputSelected));
        OnPropertyChanged(nameof(IsScreenInputSelected));

        if (IsCameraInputSelected && CameraDevices.Count == 0)
        {
            _ = RefreshVideoDevicesAsync();
        }

        if (IsScreenInputSelected && ScreenCaptureTargets.Count == 0)
        {
            _ = RefreshVideoDevicesAsync();
        }

        QueueActiveVideoInputHotSwitch("输入模式已切换");
    }

    partial void OnLocalPreviewVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(LocalPreviewToggleText));
    }

    partial void OnRemoteVideoFullscreenChanged(bool value)
    {
        OnPropertyChanged(nameof(RemoteVideoFullscreenButtonText));
    }

    partial void OnSelectedPublicLiveRoomChanged(LiveRoomDto? value)
    {
        if (value is not null && !IsLiveRoomActive)
        {
            LiveRoomCodeInput = value.RoomId;
        }
    }

    partial void OnSelectedCameraDeviceChanged(VideoCaptureDeviceOption? value)
    {
        QueueActiveVideoInputHotSwitch("摄像头已切换");
    }

    partial void OnSelectedScreenCaptureTargetChanged(ScreenCaptureTargetOption? value)
    {
        QueueActiveVideoInputHotSwitch("共享目标已切换");
    }

    partial void OnSelectedMicrophoneDeviceChanged(AudioInputDeviceOption? value)
    {
        UpdateAudioSelectionText();
        QueueActiveAudioInputHotSwitch("麦克风已切换");
    }

    partial void OnSelectedSystemAudioDeviceChanged(AudioInputDeviceOption? value)
    {
        UpdateAudioSelectionText();
        QueueActiveAudioInputHotSwitch("系统音频设备已切换");
    }

    partial void OnMixSystemAudioEnabledChanged(bool value)
    {
        UpdateAudioSelectionText();
        QueueActiveAudioInputHotSwitch(value ? "系统音频混入已开启" : "系统音频混入已关闭");
    }

    partial void OnSelectedAudioBitrateKbpsChanged(int value)
    {
        OnPropertyChanged(nameof(MediaQualitySelectionText));
        QueueActiveAudioInputHotSwitch("音频码率已切换");
    }

    partial void OnSelectedCameraBitrateKbpsChanged(int value)
    {
        OnPropertyChanged(nameof(MediaQualitySelectionText));
        QueueActiveVideoInputHotSwitch("摄像头码率已切换");
    }

    partial void OnSelectedCameraFrameRateChanged(int value)
    {
        OnPropertyChanged(nameof(MediaQualitySelectionText));
        QueueActiveVideoInputHotSwitch("摄像头帧率已切换");
    }

    partial void OnSelectedScreenShareBitrateKbpsChanged(int value)
    {
        OnPropertyChanged(nameof(MediaQualitySelectionText));
        QueueActiveVideoInputHotSwitch("屏幕共享码率已切换");
    }

    partial void OnSelectedScreenShareFrameRateChanged(int value)
    {
        OnPropertyChanged(nameof(MediaQualitySelectionText));
        QueueActiveVideoInputHotSwitch("屏幕共享帧率已切换");
    }

    partial void OnSelectedOnlineUserChanged(OnlineUserItem? value)
    {
        if (value is not null)
        {
            RecipientUserId = value.UserId;
        }
    }

    public async Task InitializeAsync()
    {
        var capabilities = await _videoCallService.GetCapabilitiesAsync();
        VideoStages.Clear();
        foreach (var stage in capabilities.PlannedStages)
        {
            VideoStages.Add(stage);
        }

        VideoStatusText = $"信令: {capabilities.SignalTransport} | 媒体: {capabilities.MediaTransport}";
        LiveRoomStages.Clear();
        LiveRoomStages.Add("直播房间入口已预留。") ;
        LiveRoomStages.Add("后续可在房间服务中登记主播与观众。") ;
        LiveRoomStages.Add("后续可接入 SFU/转推服务做一对多分发。") ;
        await RefreshVideoDevicesAsync();
        await RefreshLiveRoomsAsync();
        await RefreshOnlineUsersAsync();
        await RefreshPendingCallsAsync();
    }

    public async Task CreateLiveRoomAsync()
    {
        var owner = string.IsNullOrWhiteSpace(CurrentUserId) ? "未登录用户" : CurrentUserId.Trim();
        try
        {
            await ResetLiveRoomExperienceAsync();
            var room = await _liveBroadcastService.CreateRoomAsync(new CreateLiveRoomRequest
            {
                HostUserId = owner,
                HostDeviceId = Environment.MachineName,
                DisplayName = LiveRoomName.Trim(),
                IsPublic = true
            });

            _activeLiveRoomId = room.RoomId;
            _isLiveRoomHost = true;
            LiveRoomCodeInput = room.RoomId;
            ApplyActiveLiveRoomState(room, isHost: true);
            LiveRoomStatusText = $"已创建直播房间 {room.RoomId}。当前已具备对外接口能力，后续可继续接入真实推流 / SFU 分发。";
            ReplaceLiveRoomStages(
                $"房间已创建: {room.RoomId}",
                $"主播: {room.HostUserId}",
                "外部系统现在可以通过直播接口创建 / 查询 / 加入该房间。",
                "当前会直接启动房间推流，观众加入后即可建立观看链路。 ");
            StatusText = $"已创建直播房间 {room.RoomId}。";
            OnPropertyChanged(nameof(IsLiveRoomActive));
            OnPropertyChanged(nameof(IsLiveRoomHost));
            OnPropertyChanged(nameof(ShowLiveHostLayout));
            OnPropertyChanged(nameof(ShowLiveAudienceLayout));
            OnPropertyChanged(nameof(LiveRoomVideoFrame));
            await RefreshLiveRoomsAsync();
            await StartLiveRoomSessionAsync(room, isHost: true);
        }
        catch (Exception ex)
        {
            LiveRoomStatusText = $"创建直播房间失败: {ex.Message}";
            StatusText = LiveRoomStatusText;
        }
    }

    public async Task JoinLiveRoomAsync()
    {
        var roomId = LiveRoomCodeInput.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(roomId))
        {
            LiveRoomStatusText = "请输入要进入观看的直播房间号。";
            return;
        }

        try
        {
            await ResetLiveRoomExperienceAsync();
            var room = await _liveBroadcastService.JoinRoomAsync(roomId, new JoinLiveRoomRequest
            {
                UserId = string.IsNullOrWhiteSpace(CurrentUserId) ? "未登录用户" : CurrentUserId.Trim(),
                DeviceId = Environment.MachineName
            });

            _activeLiveRoomId = room.RoomId;
            _isLiveRoomHost = string.Equals(room.HostUserId, CurrentUserId.Trim(), StringComparison.OrdinalIgnoreCase);
            ApplyActiveLiveRoomState(room, _isLiveRoomHost);
            LiveRoomStatusText = $"已进入直播房间 {room.RoomId}。当前房间观众数: {room.ViewerCount}。后续可继续接入实际拉流 / SFU 分发。";
            ReplaceLiveRoomStages(
                $"已选择直播房间: {room.RoomId}",
                $"当前角色: {(_isLiveRoomHost ? "主播" : "观众")}",
                $"当前观众数: {room.ViewerCount}",
                "当前会直接接入房间 WebRTC 观看链路。 ");
            StatusText = $"已进入直播房间 {room.RoomId}。";
            OnPropertyChanged(nameof(IsLiveRoomActive));
            OnPropertyChanged(nameof(IsLiveRoomHost));
            OnPropertyChanged(nameof(ShowLiveHostLayout));
            OnPropertyChanged(nameof(ShowLiveAudienceLayout));
            OnPropertyChanged(nameof(LiveRoomVideoFrame));
            await RefreshLiveRoomsAsync();
            await StartLiveRoomSessionAsync(room, isHost: _isLiveRoomHost);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LiveRoomStatusText = $"加入直播房间失败：房间 {roomId} 不存在，或当前连接的服务器不是主播所在服务器。请确认双方连接的是同一台服务端，例如本地测试时统一使用 http://localhost:5000。";
            StatusText = LiveRoomStatusText;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            LiveRoomStatusText = $"加入直播房间失败：{ex.Message}";
            StatusText = LiveRoomStatusText;
        }
        catch (Exception ex)
        {
            LiveRoomStatusText = $"加入直播房间失败: {ex.Message}";
            StatusText = LiveRoomStatusText;
        }
    }

    public async Task LeaveLiveRoomAsync()
    {
        if (!IsLiveRoomActive)
        {
            LiveRoomStatusText = "当前没有可离开的直播房间。";
            return;
        }

        var roomId = _activeLiveRoomId!;
        try
        {
            await _liveBroadcastService.LeaveRoomAsync(roomId, new LeaveLiveRoomRequest
            {
                UserId = string.IsNullOrWhiteSpace(CurrentUserId) ? "未登录用户" : CurrentUserId.Trim(),
                DeviceId = Environment.MachineName
            });

            await ResetLiveRoomExperienceAsync();

            _activeLiveRoomId = null;
            _isLiveRoomHost = false;
            ClearActiveLiveRoomState();
            LiveRoomStatusText = $"已离开直播房间 {roomId}。当前直播间接口仍可继续被外部调用。";
            ReplaceLiveRoomStages(
                $"已离开直播房间: {roomId}",
                "直播接口仍可供外部调用。",
                "后续可继续接入真实直播发布与观看。 ");
            StatusText = $"已离开直播房间 {roomId}。";
            OnPropertyChanged(nameof(IsLiveRoomActive));
            OnPropertyChanged(nameof(IsLiveRoomHost));
            OnPropertyChanged(nameof(ShowLiveHostLayout));
            OnPropertyChanged(nameof(ShowLiveAudienceLayout));
            OnPropertyChanged(nameof(LiveRoomVideoFrame));
            await RefreshLiveRoomsAsync();
        }
        catch (Exception ex)
        {
            LiveRoomStatusText = $"离开直播房间失败: {ex.Message}";
            StatusText = LiveRoomStatusText;
        }
    }

    public async Task JoinSelectedLiveRoomAsync()
    {
        if (SelectedPublicLiveRoom is null)
        {
            LiveRoomStatusText = "请先从直播目录中选择一个房间。";
            return;
        }

        LiveRoomCodeInput = SelectedPublicLiveRoom.RoomId;
        await JoinLiveRoomAsync();
    }

    public async Task RefreshLiveRoomsAsync()
    {
        try
        {
            var rooms = await _liveBroadcastService.GetPublicRoomsAsync();
            var selectedRoomId = SelectedPublicLiveRoom?.RoomId;

            PublicLiveRooms.Clear();
            foreach (var room in rooms.OrderByDescending(room => room.ViewerCount).ThenByDescending(room => room.UpdatedAtUtc))
            {
                PublicLiveRooms.Add(room);
            }

            SelectedPublicLiveRoom = PublicLiveRooms.FirstOrDefault(room => string.Equals(room.RoomId, selectedRoomId, StringComparison.OrdinalIgnoreCase))
                                   ?? PublicLiveRooms.FirstOrDefault(room => string.Equals(room.RoomId, _activeLiveRoomId, StringComparison.OrdinalIgnoreCase))
                                   ?? PublicLiveRooms.FirstOrDefault();

            LiveRoomDirectoryStatusText = PublicLiveRooms.Count == 0
                ? "当前没有公开直播间，创建后会显示在这里。"
                : $"已刷新 {PublicLiveRooms.Count} 个公开直播间。";
            OnPropertyChanged(nameof(HasPublicLiveRooms));
        }
        catch (Exception ex)
        {
            LiveRoomDirectoryStatusText = $"直播目录刷新失败: {ex.Message}";
        }
    }

    public Task RefreshVideoDevicesAsync()
    {
        try
        {
            _suppressActiveInputHotSwitch = true;
            var devices = _videoDeviceDiscoveryService.GetCameraDevices();
            var screenTargets = _screenCaptureTargetDiscoveryService.GetTargets();
            var audioInputs = _audioInputDeviceDiscoveryService.GetInputDevices();
            var previouslySelectedPath = SelectedCameraDevice?.DevicePath;
            var previouslySelectedScreenPath = SelectedScreenCaptureTarget?.SourcePath;
            var previouslySelectedMicrophone = SelectedMicrophoneDevice?.DeviceNumber;
            var previouslySelectedSystemAudio = SelectedSystemAudioDevice?.DeviceId;

            CameraDevices.Clear();
            foreach (var device in devices)
            {
                CameraDevices.Add(device);
            }

            ScreenCaptureTargets.Clear();
            foreach (var target in screenTargets)
            {
                ScreenCaptureTargets.Add(target);
            }

            MicrophoneDevices.Clear();
            foreach (var input in audioInputs.Where(item => item.IsMicrophone))
            {
                MicrophoneDevices.Add(input);
            }

            SystemAudioDevices.Clear();
            foreach (var input in audioInputs.Where(item => item.IsSystemLoopback))
            {
                SystemAudioDevices.Add(input);
            }

            SelectedCameraDevice = CameraDevices.FirstOrDefault(device =>
                                     string.Equals(device.DevicePath, previouslySelectedPath, StringComparison.OrdinalIgnoreCase))
                                 ?? GetPreferredCameraDevice(CameraDevices);

            SelectedScreenCaptureTarget = ScreenCaptureTargets.FirstOrDefault(target =>
                                              string.Equals(target.SourcePath, previouslySelectedScreenPath, StringComparison.OrdinalIgnoreCase))
                                          ?? ScreenCaptureTargets.FirstOrDefault();

            SelectedMicrophoneDevice = MicrophoneDevices.FirstOrDefault(device => device.DeviceNumber == previouslySelectedMicrophone)
                                       ?? MicrophoneDevices.FirstOrDefault();

            SelectedSystemAudioDevice = SystemAudioDevices.FirstOrDefault(device =>
                                            string.Equals(device.DeviceId, previouslySelectedSystemAudio, StringComparison.OrdinalIgnoreCase))
                                        ?? SystemAudioDevices.FirstOrDefault();

            CameraSelectionText = CameraDevices.Count == 0
                ? "未检测到可用摄像头。可以刷新设备，或切换到屏幕共享。"
                : SelectedCameraDevice is null
                    ? $"已检测到 {CameraDevices.Count} 个摄像头，请选择一个设备。"
                    : $"已检测到 {CameraDevices.Count} 个摄像头，当前选择: {SelectedCameraDevice.DisplayName}";

            ScreenCaptureSelectionText = ScreenCaptureTargets.Count == 0
                ? "未检测到可共享窗口，当前将只能共享整个桌面。"
                : SelectedScreenCaptureTarget is null
                    ? $"已检测到 {ScreenCaptureTargets.Count} 个可共享目标，请选择窗口或整个桌面。"
                    : $"当前共享目标: {SelectedScreenCaptureTarget.DisplayName}";

            UpdateAudioSelectionText();
        }
        catch (Exception ex)
        {
            CameraDevices.Clear();
            ScreenCaptureTargets.Clear();
            MicrophoneDevices.Clear();
            SystemAudioDevices.Clear();
            SelectedCameraDevice = null;
            SelectedScreenCaptureTarget = null;
            SelectedMicrophoneDevice = null;
            SelectedSystemAudioDevice = null;
            CameraSelectionText = $"摄像头枚举失败: {ex.Message}";
            ScreenCaptureSelectionText = $"共享目标枚举失败: {ex.Message}";
            AudioInputSelectionText = $"音频输入枚举失败: {ex.Message}";
        }
        finally
        {
            _suppressActiveInputHotSwitch = false;
        }

        if (IsCallActive)
        {
            QueueActiveVideoInputHotSwitch("设备列表已刷新");
            QueueActiveAudioInputHotSwitch("设备列表已刷新");
        }

        return Task.CompletedTask;
    }

    public async Task SignInAsync()
    {
        try
        {
            StatusText = "正在打开 Microsoft 登录...";
            var login = await _microsoftOAuthLoopbackService.SignInAsync();
            _apiClient.SetBearerToken(login.AccessToken);

            if (!string.IsNullOrWhiteSpace(login.UserId))
            {
                CurrentUserId = login.UserId;
                PendingUserId = login.UserId;
            }

            var currentUser = await _apiClient.GetCurrentUserAsync();
            if (currentUser is not null)
            {
                CurrentUserId = currentUser.UserId;
                PendingUserId = currentUser.UserId;
                CurrentDisplayName = $"{currentUser.DisplayName} <{currentUser.Email}>";
            }

            StatusText = $"已登录: {CurrentUserId}";
            _presenceTimer.Start();
            await SendPresenceHeartbeatAsync();
            await RefreshPendingCallsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"登录失败: {ex.Message}";
        }
    }

    public async Task UpdateUserIdAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CurrentUserId))
            {
                StatusText = "请先登录后再修改用户 ID。";
                return;
            }

            var requestedUserId = PendingUserId.Trim();
            if (string.IsNullOrWhiteSpace(requestedUserId))
            {
                StatusText = "请输入要保存的用户 ID。";
                return;
            }

            var previousUserId = CurrentUserId;
            var updatedUser = await _apiClient.UpdateMyUserIdAsync(requestedUserId);
            if (!string.Equals(previousUserId, updatedUser.UserId, StringComparison.OrdinalIgnoreCase))
            {
                _identityKeyService.RenameIdentity(previousUserId, updatedUser.UserId);
            }

            CurrentUserId = updatedUser.UserId;
            PendingUserId = updatedUser.UserId;
            CurrentDisplayName = $"{updatedUser.DisplayName} <{updatedUser.Email}>";
            StatusText = $"用户 ID 已更新为: {updatedUser.UserId}";
            await RefreshOnlineUsersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"更新用户 ID 失败: {ex.Message}";
        }
    }

    public async Task RegisterPublicKeyAsync()
    {
        try
        {
            var userId = CurrentUserId.Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                StatusText = "请先填写或登录 Current User ID。";
                return;
            }

            _identityBootstrapService.EnsureInitialized(userId);
            var publicKeyPem = _identityKeyService.LoadPublicKeyPem(userId);
            await _apiClient.RegisterPublicKeyAsync(userId, publicKeyPem);
            StatusText = "公钥已注册。";
            await RefreshOnlineUsersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"注册公钥失败: {ex.Message}";
        }
    }

    public async Task SendMessageAsync()
    {
        try
        {
            var senderUserId = CurrentUserId.Trim();
            var targetUserId = RecipientUserId.Trim();
            var plaintext = MessageText.Trim();
            var pendingAttachment = _pendingAttachment;

            if (string.IsNullOrWhiteSpace(senderUserId)
                || string.IsNullOrWhiteSpace(targetUserId)
                || (string.IsNullOrWhiteSpace(plaintext) && pendingAttachment is null))
            {
                StatusText = "当前用户、接收用户，以及消息内容或附件至少要提供一项。";
                return;
            }

            _identityBootstrapService.EnsureInitialized(senderUserId);
            var recipientPublicKeyPem = await _apiClient.GetPublicKeyAsync(targetUserId);
            if (string.IsNullOrWhiteSpace(recipientPublicKeyPem))
            {
                StatusText = "未找到对方公钥，请先让对方注册。";
                return;
            }

            using var recipientRsa = _identityKeyService.LoadPublicKeyFromPem(recipientPublicKeyPem);
            using var senderPrivateKey = _identityKeyService.LoadPrivateKey(senderUserId);
            var senderPublicKeyPem = _identityKeyService.LoadPublicKeyPem(senderUserId);

            var metadata = new ChatMessageMetadata
            {
                MessageId = Guid.NewGuid().ToString("N"),
                ConversationId = BuildConversationId(senderUserId, targetUserId),
                SenderUserId = senderUserId,
                SenderDeviceId = Environment.MachineName,
                RecipientUserId = targetUserId,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MessageType = pendingAttachment is null
                    ? "text"
                    : string.IsNullOrWhiteSpace(plaintext)
                        ? "attachment"
                        : "text+attachment"
            };

            var protectedContent = pendingAttachment is null
                ? plaintext
                : JsonSerializer.Serialize(new MessageContentPayload
                {
                    Text = plaintext,
                    Attachment = pendingAttachment
                });

            var envelope = _messageCryptoService.EncryptText(
                protectedContent,
                metadata,
                recipientRsa,
                senderPrivateKey,
                senderPublicKeyPem);

            await _apiClient.SendMessageAsync(new SendMessageRequest
            {
                SenderUserId = senderUserId,
                RecipientUserId = targetUserId,
                EnvelopeJson = _messageCryptoService.SerializeEnvelope(envelope)
            });

            MessageText = string.Empty;
            ClearPendingAttachment();
            StatusText = pendingAttachment is null ? "消息已加密发送。" : "消息和附件已加密发送。";
            await RefreshOnlineUsersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"发送失败: {ex.Message}";
        }
    }

    public async Task RefreshInboxAsync()
    {
        try
        {
            var userId = CurrentUserId.Trim();
            if (string.IsNullOrWhiteSpace(userId))
            {
                StatusText = "请先填写或登录 Current User ID。";
                return;
            }

            _identityBootstrapService.EnsureInitialized(userId);
            using var privateKey = _identityKeyService.LoadPrivateKey(userId);

            var inbox = await _apiClient.GetInboxAsync(userId);
            Messages.Clear();

            foreach (var item in inbox.OrderByDescending(message => message.CreatedAt))
            {
                var envelope = _messageCryptoService.DeserializeEnvelope(item.EnvelopeJson);
                var summary = $"[{item.CreatedAt:yyyy-MM-dd HH:mm:ss}] {item.SenderUserId}";
                string detail;

                if (string.Equals(envelope.Metadata.MessageType, "text", StringComparison.OrdinalIgnoreCase))
                {
                    var decrypted = _messageCryptoService.DecryptText(envelope, privateKey);
                    summary = decrypted.Success ? summary : $"{summary} <decrypt failed>";
                    detail = decrypted.Success
                        ? $"{decrypted.Plaintext} | 签名: {(decrypted.SignatureValid ? "ok" : "invalid")}"
                        : decrypted.Error;

                    Messages.Add(new MessageListItem(summary, detail));
                }
                else
                {
                    var decrypted = _messageCryptoService.DecryptText(envelope, privateKey);
                    if (!decrypted.Success)
                    {
                        summary = $"{summary} <decrypt failed>";
                        detail = decrypted.Error;
                    }
                    else
                    {
                        var payload = JsonSerializer.Deserialize<MessageContentPayload>(decrypted.Plaintext);
                        if (payload is null)
                        {
                            summary = $"{summary} <payload invalid>";
                            detail = "附件消息负载无法解析。";
                        }
                        else
                        {
                            detail = BuildAttachmentMessageDetail(payload, decrypted.SignatureValid);
                            Messages.Add(new MessageListItem(summary, detail, payload.Attachment));
                        }
                    }

                    if (!decrypted.Success || detail == "附件消息负载无法解析。")
                    {
                        Messages.Add(new MessageListItem(summary, detail));
                    }
                }
            }

            StatusText = $"收件箱已刷新，共 {inbox.Count} 条。";
            await RefreshOnlineUsersAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"刷新收件箱失败: {ex.Message}";
        }
    }

    public async Task RefreshOnlineUsersAsync()
    {
        try
        {
            var stats = await _apiClient.GetOnlineStatsAsync();
            if (stats is null)
            {
                return;
            }

            ApplyOnlineStats(stats);
            FooterText = $"在线用户 {stats.OnlineUserCount} / 活跃设备 {stats.ActiveDeviceCount}";
            await RefreshPendingCallsAsync();
        }
        catch
        {
            // Keep the last online state when the stats endpoint is unavailable.
        }
    }

    public async Task PrepareVideoCallAsync()
    {
        try
        {
            var callerUserId = CurrentUserId.Trim();
            var targetUserId = RecipientUserId.Trim();
            if (string.IsNullOrWhiteSpace(callerUserId) || string.IsNullOrWhiteSpace(targetUserId))
            {
                StatusText = "发起视频通话前需要当前用户和接收用户。";
                return;
            }

            await ResetCallExperienceAsync();

            var response = await _apiClient.StartCallAsync(new StartCallRequest
            {
                CallerUserId = callerUserId,
                RecipientUserId = targetUserId,
                CallerDeviceId = Environment.MachineName,
                AudioEnabled = true,
                VideoEnabled = true
            });

            if (response is null || string.IsNullOrWhiteSpace(response.CallId))
            {
                StatusText = "服务端未返回有效的呼叫会话。";
                return;
            }

            var cts = new CancellationTokenSource();
            var socket = await _apiClient.ConnectCallSignalWebSocketAsync(response.CallId, callerUserId, cts.Token);
            _callSignalSocket = socket;
            _callSignalCts = cts;
            _activeCallId = response.CallId;
            ActiveCallPeerText = $"通话对象: {targetUserId}";
            _mediaSession = CreateMediaSession(response.CallId);
            OnPropertyChanged(nameof(IsCallActive));
            VideoStages.Clear();

            await _mediaSession.InitializeAsync(
                response.CallId,
                callerUserId,
                isCaller: true,
                GetSelectedCaptureMode(),
                GetSelectedCameraDevicePath(),
                GetSelectedScreenCaptureTarget(),
                GetSelectedMicrophoneDevice(),
                GetSelectedSystemAudioDevice(),
                response.IceServers,
                cts.Token);
            _ = Task.Run(() => ReceiveCallSignalsAsync(response.CallId, socket, cts.Token));

            TrackVideoStage($"CallId: {response.CallId}");
            TrackVideoStage($"Signaling: {response.SignalTransport}");
            TrackVideoStage($"WebSocket: connected as {callerUserId}");
            TrackVideoStage($"Recipient: {targetUserId}");

            VideoStatusText = $"已创建呼叫 {response.CallId}，并启动 WebRTC 视频会话。";
            StatusText = $"已向 {targetUserId} 发起 WebRTC 视频呼叫。";
            await RefreshPendingCallsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"创建视频通话失败: {ex.Message}";
        }
    }

    public async Task AnswerIncomingCallAsync()
    {
        try
        {
            var localUserId = CurrentUserId.Trim();
            if (string.IsNullOrWhiteSpace(localUserId))
            {
                StatusText = "请先登录后再接听来电。";
                return;
            }

            var pendingCall = _pendingIncomingCall;
            if (pendingCall is null)
            {
                StatusText = "当前没有待接听来电。";
                return;
            }

            await ResetCallExperienceAsync();

            var cts = new CancellationTokenSource();
            var socket = await _apiClient.ConnectCallSignalWebSocketAsync(pendingCall.CallId, localUserId, cts.Token);
            _callSignalSocket = socket;
            _callSignalCts = cts;
            _activeCallId = pendingCall.CallId;
            ActiveCallPeerText = $"通话对象: {pendingCall.CallerUserId}";
            _mediaSession = CreateMediaSession(pendingCall.CallId);
            OnPropertyChanged(nameof(IsCallActive));
            VideoStages.Clear();

            await _mediaSession.InitializeAsync(
                pendingCall.CallId,
                localUserId,
                isCaller: false,
                GetSelectedCaptureMode(),
                GetSelectedCameraDevicePath(),
                GetSelectedScreenCaptureTarget(),
                GetSelectedMicrophoneDevice(),
                GetSelectedSystemAudioDevice(),
                [],
                cts.Token);
            _ = Task.Run(() => ReceiveCallSignalsAsync(pendingCall.CallId, socket, cts.Token));

            TrackVideoStage($"Joined incoming call from {pendingCall.CallerUserId}");
            VideoStatusText = $"已加入来自 {pendingCall.CallerUserId} 的呼叫 {pendingCall.CallId}。";
            StatusText = $"已接听来自 {pendingCall.CallerUserId} 的视频呼叫。";
            await RefreshPendingCallsAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"接听来电失败: {ex.Message}";
        }
    }

    private async Task SendPresenceHeartbeatAsync()
    {
        var userId = CurrentUserId.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        try
        {
            await _apiClient.SendHeartbeatAsync(userId, Environment.MachineName);
            await RefreshOnlineUsersAsync();
            await RefreshPendingCallsAsync();
        }
        catch
        {
            // Heartbeat is best-effort and should not interrupt chat flow.
        }
    }

    private void ApplyOnlineStats(OnlineStatsResponse stats)
    {
        OnlineUsers.Clear();
        foreach (var user in stats.Users.OrderBy(entry => entry.UserId))
        {
            var presence = $"{user.DeviceCount} 台设备，最近活跃 {user.LastSeenUtc.LocalDateTime:MM-dd HH:mm:ss}";
            OnlineUsers.Add(new OnlineUserItem(user.UserId, presence));
        }
    }

    private static string BuildConversationId(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}:{b}" : $"{b}:{a}";
    }

    public void SetPendingAttachment(string fileName, byte[] contentBytes, string contentType)
    {
        _pendingAttachment = new MessageAttachmentPayload
        {
            FileName = fileName,
            ContentType = contentType,
            ContentBytes = contentBytes,
            ByteLength = contentBytes.LongLength
        };

        PendingAttachmentSummary = $"已选择附件: {fileName} ({FormatByteSize(contentBytes.LongLength)})";
        OnPropertyChanged(nameof(HasPendingAttachment));
    }

    public void ClearPendingAttachment()
    {
        _pendingAttachment = null;
        PendingAttachmentSummary = "未选择附件。";
        OnPropertyChanged(nameof(HasPendingAttachment));
    }

    public async ValueTask DisposeAsync()
    {
        _presenceTimer.Stop();
        await ResetLiveRoomExperienceAsync();
        await ResetCallExperienceAsync();
    }

    public async Task HangupCallAsync()
    {
        if (!IsCallActive)
        {
            VideoStatusText = "当前没有进行中的通话。";
            return;
        }

        var peerText = ActiveCallPeerText;
        if (!string.IsNullOrWhiteSpace(_activeCallId))
        {
            try
            {
                await SendCallSignalAsync(new CallSignalRequest
                {
                    CallId = _activeCallId,
                    SenderUserId = string.IsNullOrWhiteSpace(CurrentUserId) ? "未登录用户" : CurrentUserId.Trim(),
                    SignalType = "hangup",
                    PayloadJson = "{}"
                }, CancellationToken.None);
            }
            catch
            {
                // Best-effort remote hangup notification.
            }
        }

        await ResetCallExperienceAsync();
        TrackVideoStage("Call: hangup requested");
        VideoStatusText = "通话已挂断。";
        StatusText = string.Equals(peerText, "当前未在通话中。", StringComparison.Ordinal)
            ? "已结束当前通话。"
            : $"已挂断与 {peerText.Replace("通话对象: ", string.Empty, StringComparison.Ordinal)} 的通话。";
    }

    private async Task SendCallSignalAsync(CallSignalRequest request, CancellationToken cancellationToken)
    {
        if (_callSignalSocket is not { State: WebSocketState.Open } socket)
        {
            await Dispatcher.UIThread.InvokeAsync(() => TrackVideoStage($"信令发送失败，WebSocket 未打开: {request.SignalType}"));
            return;
        }

        var payload = JsonSerializer.Serialize(request, CallSignalJsonOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        await Dispatcher.UIThread.InvokeAsync(() => TrackVideoStage($"信令已发送: {request.SignalType}"));
    }

    private async Task ReceiveCallSignalsAsync(string callId, ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            VideoStatusText = $"呼叫 {callId} 的信令连接已关闭。";
                            TrackVideoStage("WebSocket: disconnected");
                        });
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                var signal = JsonSerializer.Deserialize<CallSignalMessageDto>(json, CallSignalJsonOptions);
                if (signal is null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(signal.CallId) || string.IsNullOrWhiteSpace(signal.SignalType))
                {
                    await Dispatcher.UIThread.InvokeAsync(() => TrackVideoStage($"忽略无效信令 JSON: {json}"));
                    continue;
                }

                if (string.Equals(signal.SignalType, "hangup", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(signal.SenderUserId, CurrentUserId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    await ResetCallExperienceAsync();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        VideoStatusText = $"通话已结束，对方 {signal.SenderUserId} 已挂断。";
                        StatusText = VideoStatusText;
                        TrackVideoStage($"{signal.CreatedAtUtc.LocalDateTime:HH:mm:ss} {signal.SenderUserId}: hangup");
                    });
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    VideoStatusText = $"呼叫 {signal.CallId} 已连接 WebSocket 信令，最近消息: {signal.SignalType}";
                    TrackVideoStage($"{signal.CreatedAtUtc.LocalDateTime:HH:mm:ss} {signal.SenderUserId}: {signal.SignalType}");
                });

                if (_mediaSession is not null)
                {
                    await _mediaSession.HandleSignalAsync(signal, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Window closing or call reset.
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VideoStatusText = $"呼叫 {callId} 的信令接收失败: {ex.Message}";
                TrackVideoStage("WebSocket: receive loop failed");
            });
        }
    }

    private async Task ResetCallSignalConnectionAsync()
    {
        var socket = _callSignalSocket;
        var cts = _callSignalCts;

        _callSignalSocket = null;
        _callSignalCts = null;
        _activeCallId = null;
        ActiveCallPeerText = "当前未在通话中。";
        OnPropertyChanged(nameof(IsCallActive));

        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (socket is null)
        {
            return;
        }

        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Call session reset.", CancellationToken.None);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
        finally
        {
            socket.Dispose();
        }
    }

    private WebRtcMediaSession CreateMediaSession(string callId)
    {
        return new WebRtcMediaSession(
            (request, cancellationToken) => SendCallSignalAsync(request, cancellationToken),
            () => BuildMediaSessionTuningOptions(),
            status => Dispatcher.UIThread.Post(() => VideoStatusText = $"呼叫 {callId} | {status}"),
            stage => Dispatcher.UIThread.Post(() => TrackVideoStage(stage)),
            localFrame => Dispatcher.UIThread.Post(() => LocalVideoFrame = localFrame),
            remoteFrame => Dispatcher.UIThread.Post(() => RemoteVideoFrame = remoteFrame));
    }

    private async Task RefreshPendingCallsAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            _pendingIncomingCall = null;
            IncomingCallText = "暂无待接听来电。";
            return;
        }

        var pendingCalls = await _apiClient.GetPendingCallsAsync(CurrentUserId);
        _pendingIncomingCall = pendingCalls
            .FirstOrDefault(call => !string.Equals(call.CallId, _activeCallId, StringComparison.OrdinalIgnoreCase));

        IncomingCallText = _pendingIncomingCall is null
            ? "暂无待接听来电。"
            : $"来自 {_pendingIncomingCall.CallerUserId} 的来电，发起时间 {_pendingIncomingCall.CreatedAtUtc.LocalDateTime:HH:mm:ss}";
    }

    private async Task ResetCallExperienceAsync()
    {
        await ResetMediaSessionAsync();
        await ResetCallSignalConnectionAsync();
    }

    private async Task ResetMediaSessionAsync()
    {
        if (_mediaSession is not null)
        {
            await _mediaSession.DisposeAsync();
            _mediaSession = null;
        }

        LocalVideoFrame = null;
        RemoteVideoFrame = null;
        OnPropertyChanged(nameof(IsCallActive));
    }

    private VideoCaptureMode GetSelectedCaptureMode()
    {
        return string.Equals(SelectedVideoInputMode, "屏幕共享", StringComparison.OrdinalIgnoreCase)
            ? VideoCaptureMode.Screen
            : VideoCaptureMode.Camera;
    }

    private string? GetSelectedCameraDevicePath()
    {
        return GetSelectedCaptureMode() == VideoCaptureMode.Camera
            ? SelectedCameraDevice?.DevicePath
            : null;
    }

    private static VideoCaptureDeviceOption? GetPreferredCameraDevice(IEnumerable<VideoCaptureDeviceOption> devices)
    {
        return devices
            .OrderBy(GetCameraPreferenceScore)
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }

    private static int GetCameraPreferenceScore(VideoCaptureDeviceOption device)
    {
        var name = device.DisplayName;
        if (name.Contains("virtual", StringComparison.OrdinalIgnoreCase)
            || name.Contains("broadcast", StringComparison.OrdinalIgnoreCase)
            || name.Contains("obs", StringComparison.OrdinalIgnoreCase)
            || name.Contains("snap", StringComparison.OrdinalIgnoreCase)
            || name.Contains("manycam", StringComparison.OrdinalIgnoreCase)
            || name.Contains("droidcam", StringComparison.OrdinalIgnoreCase)
            || name.Contains("epoccam", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (name.Contains("usb", StringComparison.OrdinalIgnoreCase)
            || name.Contains("uvc", StringComparison.OrdinalIgnoreCase)
            || name.Contains("webcam", StringComparison.OrdinalIgnoreCase)
            || name.Contains("integrated", StringComparison.OrdinalIgnoreCase)
            || name.Contains("camera", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return 10;
    }

    private ScreenCaptureTargetOption? GetSelectedScreenCaptureTarget()
    {
        return GetSelectedCaptureMode() == VideoCaptureMode.Screen
            ? SelectedScreenCaptureTarget
            : null;
    }

    private AudioInputDeviceOption? GetSelectedMicrophoneDevice()
    {
        return SelectedMicrophoneDevice;
    }

    private AudioInputDeviceOption? GetSelectedSystemAudioDevice()
    {
        return MixSystemAudioEnabled ? SelectedSystemAudioDevice : null;
    }

    private void UpdateAudioSelectionText()
    {
        var microphoneText = MicrophoneDevices.Count == 0
            ? "未检测到麦克风。"
            : SelectedMicrophoneDevice is null
                ? $"已检测到 {MicrophoneDevices.Count} 个麦克风，请选择一个设备。"
                : $"当前麦克风: {SelectedMicrophoneDevice.DisplayName}";

        var systemAudioText = SystemAudioDevices.Count == 0
            ? "未检测到可混入的系统音频输出设备。"
            : !MixSystemAudioEnabled
                ? "系统音频混入未启用。"
                : SelectedSystemAudioDevice is null
                    ? $"已启用系统音频混入，可选择 {SystemAudioDevices.Count} 个输出设备。"
                    : $"当前系统音频: {SelectedSystemAudioDevice.DisplayName}。对方语音将优先走系统通信输出；若系统的多媒体输出与通信输出不是同一设备，可避免把对方声音再次混入。";

        AudioInputSelectionText = $"{microphoneText} {systemAudioText}";
    }

    private MediaSessionTuningOptions BuildMediaSessionTuningOptions()
    {
        return new MediaSessionTuningOptions
        {
            AudioBitrate = SelectedAudioBitrateKbps * 1000,
            CameraVideoBitrate = SelectedCameraBitrateKbps * 1000,
            CameraFrameRate = SelectedCameraFrameRate,
            ScreenShareBitrate = SelectedScreenShareBitrateKbps * 1000,
            ScreenShareFrameRate = SelectedScreenShareFrameRate
        };
    }

    private void QueueActiveVideoInputHotSwitch(string reason)
    {
        if (_suppressActiveInputHotSwitch || _mediaSession is null || !IsCallActive)
        {
            return;
        }

        _ = ApplyActiveVideoInputAsync(reason);
    }

    private void QueueActiveAudioInputHotSwitch(string reason)
    {
        if (_suppressActiveInputHotSwitch || _mediaSession is null || !IsCallActive)
        {
            return;
        }

        _ = ApplyActiveAudioInputAsync(reason);
    }

    private async Task ApplyActiveVideoInputAsync(string reason)
    {
        if ((_mediaSession is null || !IsCallActive) && (_liveMediaSession is null || !IsLivePublishing))
        {
            return;
        }

        try
        {
            if (_mediaSession is not null && IsCallActive)
            {
                await _mediaSession.SwitchVideoInputAsync(
                    GetSelectedCaptureMode(),
                    GetSelectedCameraDevicePath(),
                    GetSelectedScreenCaptureTarget());
            }

            if (_liveMediaSession is not null && IsLivePublishing)
            {
                await _liveMediaSession.SwitchVideoInputAsync(
                    GetSelectedCaptureMode(),
                    GetSelectedCameraDevicePath(),
                    GetSelectedScreenCaptureTarget());
            }

            var targetText = GetSelectedCaptureMode() == VideoCaptureMode.Screen
                ? SelectedScreenCaptureTarget?.DisplayName ?? "整个桌面"
                : SelectedCameraDevice?.DisplayName ?? "默认摄像头";
            if (IsCallActive)
            {
                VideoStatusText = $"{reason}，已热切换视频输入到 {targetText}。";
                TrackVideoStage($"视频输入热切换完成: {targetText}");
            }

            if (IsLivePublishing)
            {
                LiveRoomStatusText = $"{reason}，直播推流已热切换到 {targetText}。";
                TrackLiveStage($"直播视频输入热切换完成: {targetText}");
            }
        }
        catch (Exception ex)
        {
            if (IsCallActive)
            {
                VideoStatusText = $"{reason}失败: {ex.Message}";
                TrackVideoStage($"视频输入热切换失败: {ex.Message}");
            }

            if (IsLivePublishing)
            {
                LiveRoomStatusText = $"{reason}失败: {ex.Message}";
                TrackLiveStage($"直播视频输入热切换失败: {ex.Message}");
            }
        }
    }

    private async Task ApplyActiveAudioInputAsync(string reason)
    {
        if ((_mediaSession is null || !IsCallActive) && (_liveMediaSession is null || !IsLivePublishing))
        {
            return;
        }

        try
        {
            if (_mediaSession is not null && IsCallActive)
            {
                await _mediaSession.SwitchAudioInputAsync(
                    GetSelectedMicrophoneDevice(),
                    GetSelectedSystemAudioDevice());
            }

            if (_liveMediaSession is not null && IsLivePublishing)
            {
                await _liveMediaSession.SwitchAudioInputAsync(
                    GetSelectedMicrophoneDevice(),
                    GetSelectedSystemAudioDevice());
            }

            var systemAudioDevice = GetSelectedSystemAudioDevice();
            var audioMode = systemAudioDevice is null
                ? SelectedMicrophoneDevice?.DisplayName ?? "未选择麦克风"
                : SelectedMicrophoneDevice is null
                    ? systemAudioDevice.DisplayName
                    : $"{SelectedMicrophoneDevice.DisplayName} + {systemAudioDevice.DisplayName}";
            if (IsCallActive)
            {
                VideoStatusText = $"{reason}，已热切换音频输入到 {audioMode}。";
                TrackVideoStage($"音频输入热切换完成: {audioMode}");
            }

            if (IsLivePublishing)
            {
                LiveRoomStatusText = $"{reason}，直播推流已热切换音频输入到 {audioMode}。";
                TrackLiveStage($"直播音频输入热切换完成: {audioMode}");
            }
        }
        catch (Exception ex)
        {
            if (IsCallActive)
            {
                VideoStatusText = $"{reason}失败: {ex.Message}";
                TrackVideoStage($"音频输入热切换失败: {ex.Message}");
            }

            if (IsLivePublishing)
            {
                LiveRoomStatusText = $"{reason}失败: {ex.Message}";
                TrackLiveStage($"直播音频输入热切换失败: {ex.Message}");
            }
        }
    }

    private void TrackVideoStage(string text)
    {
        VideoStages.Add(text);
        while (VideoStages.Count > 20)
        {
            VideoStages.RemoveAt(0);
        }
    }

    private void TrackLiveStage(string text)
    {
        LiveRoomStages.Add(text);
        while (LiveRoomStages.Count > 20)
        {
            LiveRoomStages.RemoveAt(0);
        }
    }

    private void ApplyActiveLiveRoomState(LiveRoomDto room, bool isHost)
    {
        _activeLiveRoomDisplayName = string.IsNullOrWhiteSpace(room.DisplayName) ? room.RoomId : room.DisplayName;
        _activeLiveRoomHostUserId = room.HostUserId;
        _activeLiveRoomViewerCount = room.ViewerCount;
        ActiveLiveRoomText = $"当前直播间: {_activeLiveRoomDisplayName} ({room.RoomId}) | 身份: {(isHost ? "主播" : "观众")}";
        OnPropertyChanged(nameof(ActiveLiveRoomDisplayName));
        OnPropertyChanged(nameof(ActiveLiveRoomHostText));
        OnPropertyChanged(nameof(ActiveLiveRoomViewerCountText));
        OnPropertyChanged(nameof(LiveRoomRoleBadgeText));
        OnPropertyChanged(nameof(LiveRoomRoleSummaryText));
        OnPropertyChanged(nameof(LiveRoomHeroHintText));
        OnPropertyChanged(nameof(LiveRoomPrimaryActionText));
    }

    private void ClearActiveLiveRoomState()
    {
        _activeLiveRoomDisplayName = "未进入直播间";
        _activeLiveRoomHostUserId = "-";
        _activeLiveRoomViewerCount = 0;
        ActiveLiveRoomText = "当前未加入直播间。";
        OnPropertyChanged(nameof(ActiveLiveRoomDisplayName));
        OnPropertyChanged(nameof(ActiveLiveRoomHostText));
        OnPropertyChanged(nameof(ActiveLiveRoomViewerCountText));
        OnPropertyChanged(nameof(LiveRoomRoleBadgeText));
        OnPropertyChanged(nameof(LiveRoomRoleSummaryText));
        OnPropertyChanged(nameof(LiveRoomHeroHintText));
        OnPropertyChanged(nameof(LiveRoomPrimaryActionText));
    }

    private async Task StartLiveRoomSessionAsync(LiveRoomDto room, bool isHost)
    {
        var roomId = room.RoomId;
        var localUserId = string.IsNullOrWhiteSpace(CurrentUserId) ? "未登录用户" : CurrentUserId.Trim();
        var cts = new CancellationTokenSource();
        var socket = await _apiClient.ConnectLiveRoomSignalWebSocketAsync(roomId, localUserId, cts.Token);
        _liveSignalSocket = socket;
        _liveSignalCts = cts;
        _liveMediaSession = CreateLiveMediaSession(roomId);

        await _liveMediaSession.InitializeAsync(
            roomId,
            localUserId,
            isCaller: !isHost,
            GetSelectedCaptureMode(),
            GetSelectedCameraDevicePath(),
            GetSelectedScreenCaptureTarget(),
            isHost ? GetSelectedMicrophoneDevice() : null,
            isHost ? GetSelectedSystemAudioDevice() : null,
            room.IceServers,
            isHost ? MediaSessionMode.PublishOnly : MediaSessionMode.ViewOnly,
            cts.Token);

        _ = Task.Run(() => ReceiveLiveRoomSignalsAsync(roomId, socket, cts.Token));
        TrackLiveStage(isHost ? $"直播推流已启动: {roomId}" : $"直播观看已启动: {roomId}");
        LiveRoomStatusText = isHost
            ? $"直播房间 {roomId} 已启动推流，等待观众加入并发送观看 Offer。"
            : $"已进入直播房间 {roomId}，正在建立观看链路。";
    }

    private async Task ReceiveLiveRoomSignalsAsync(string roomId, ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            LiveRoomStatusText = $"直播房间 {roomId} 的信令连接已关闭。";
                            TrackLiveStage("直播 WebSocket: disconnected");
                        });
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                var signal = JsonSerializer.Deserialize<CallSignalMessageDto>(json, CallSignalJsonOptions);
                if (signal is null || _liveMediaSession is null)
                {
                    continue;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LiveRoomStatusText = $"直播房间 {signal.CallId} 已连接 WebSocket 信令，最近消息: {signal.SignalType}";
                    TrackLiveStage($"{signal.CreatedAtUtc.LocalDateTime:HH:mm:ss} {signal.SenderUserId}: {signal.SignalType}");
                });

                await _liveMediaSession.HandleSignalAsync(signal, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LiveRoomStatusText = $"直播房间 {roomId} 的信令接收失败: {ex.Message}";
                TrackLiveStage("直播 WebSocket: receive loop failed");
            });
        }
    }

    private WebRtcMediaSession CreateLiveMediaSession(string roomId)
    {
        return new WebRtcMediaSession(
            (request, cancellationToken) => _apiClient.SendLiveRoomSignalAsync(roomId, request, cancellationToken),
            () => BuildMediaSessionTuningOptions(),
            status => Dispatcher.UIThread.Post(() => LiveRoomStatusText = $"直播 {roomId} | {status}"),
            stage => Dispatcher.UIThread.Post(() => TrackLiveStage(stage)),
            localFrame => Dispatcher.UIThread.Post(() => SetLiveLocalVideoFrame(localFrame)),
            remoteFrame => Dispatcher.UIThread.Post(() => SetLiveRemoteVideoFrame(remoteFrame)));
    }

    private async Task ResetLiveRoomExperienceAsync()
    {
        await ResetLiveMediaSessionAsync();
        await ResetLiveSignalConnectionAsync();
    }

    private async Task ResetLiveMediaSessionAsync()
    {
        if (_liveMediaSession is not null)
        {
            await _liveMediaSession.DisposeAsync();
            _liveMediaSession = null;
        }

        SetLiveLocalVideoFrame(null);
        SetLiveRemoteVideoFrame(null);
    }

    private async Task ResetLiveSignalConnectionAsync()
    {
        var socket = _liveSignalSocket;
        var cts = _liveSignalCts;

        _liveSignalSocket = null;
        _liveSignalCts = null;

        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (socket is null)
        {
            return;
        }

        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Live room session reset.", CancellationToken.None);
            }
        }
        catch
        {
        }
        finally
        {
            socket.Dispose();
        }
    }

    private void SetLiveLocalVideoFrame(Bitmap? value)
    {
        SetBitmapProperty(ref _liveLocalVideoFrame, value, nameof(_liveLocalVideoFrame));
        OnPropertyChanged(nameof(LiveRoomVideoFrame));
    }

    private void SetLiveRemoteVideoFrame(Bitmap? value)
    {
        SetBitmapProperty(ref _liveRemoteVideoFrame, value, nameof(_liveRemoteVideoFrame));
        OnPropertyChanged(nameof(LiveRoomVideoFrame));
    }

    private void ReplaceLiveRoomStages(params string[] stages)
    {
        LiveRoomStages.Clear();
        foreach (var stage in stages.Where(stage => !string.IsNullOrWhiteSpace(stage)))
        {
            LiveRoomStages.Add(stage.Trim());
        }
    }

    private void SetBitmapProperty(ref Bitmap? field, Bitmap? value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (ReferenceEquals(field, value))
        {
            return;
        }

        var previous = field;
        field = value;
        previous?.Dispose();
        OnPropertyChanged(propertyName);
    }

    private static string BuildAttachmentMessageDetail(MessageContentPayload payload, bool signatureValid)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(payload.Text))
        {
            parts.Add(payload.Text);
        }

        if (payload.Attachment is not null)
        {
            parts.Add($"附件: {payload.Attachment.FileName} ({FormatByteSize(payload.Attachment.ByteLength)})");
        }

        parts.Add($"签名: {(signatureValid ? "ok" : "invalid")}");
        return string.Join(" | ", parts);
    }

    private static string FormatByteSize(long byteCount)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = byteCount;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{size:0.##} {units[unitIndex]}");
    }
}