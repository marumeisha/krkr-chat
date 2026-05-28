using Avalonia.Controls;
using Avalonia.Interactivity;
using SecureChat.ClientCore.Services;
using SecureChat.Desktop.ViewModels;

namespace SecureChat.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private MessageWindow? _messageWindow;
    private VideoWindow? _videoWindow;
    private LiveHostWindow? _liveHostWindow;
    private LiveAudienceWindow? _liveAudienceWindow;

    public MainWindow()
        : this(ClientRuntimeOptions.Load().ApiBaseUri)
    {
    }

    public MainWindow(Uri apiBaseUri)
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(apiBaseUri);
        Opened += async (_, _) => await ViewModel.InitializeAsync();
        Closed += async (_, _) => await ViewModel.DisposeAsync();
    }

    private void OpenMessagesWindow(object? sender, RoutedEventArgs e)
    {
        if (_messageWindow is { IsVisible: true })
        {
            _messageWindow.Activate();
            return;
        }

        _messageWindow = new MessageWindow(ViewModel);
        _messageWindow.Closed += (_, _) => _messageWindow = null;
        _messageWindow.Show();
    }

    private void OpenVideoWindow(object? sender, RoutedEventArgs e)
    {
        if (_videoWindow is { IsVisible: true })
        {
            _videoWindow.Activate();
            return;
        }

        _videoWindow = new VideoWindow(ViewModel);
        _videoWindow.Closed += (_, _) => _videoWindow = null;
        _videoWindow.Show();
    }

    private void OpenLiveHostWindow(object? sender, RoutedEventArgs e)
    {
        if (_liveHostWindow is { IsVisible: true })
        {
            _liveHostWindow.Activate();
            return;
        }

        _liveHostWindow = new LiveHostWindow(ViewModel);
        _liveHostWindow.Closed += (_, _) => _liveHostWindow = null;
        _liveHostWindow.Show();
    }

    private void OpenLiveAudienceWindow(object? sender, RoutedEventArgs e)
    {
        if (_liveAudienceWindow is { IsVisible: true })
        {
            _liveAudienceWindow.Activate();
            return;
        }

        _liveAudienceWindow = new LiveAudienceWindow(ViewModel);
        _liveAudienceWindow.Closed += (_, _) => _liveAudienceWindow = null;
        _liveAudienceWindow.Show();
    }
}