using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecureChat.Desktop.ViewModels;

namespace SecureChat.Desktop;

public partial class VideoWindow : Window
{
    public VideoWindow()
    {
        InitializeComponent();
    }

    public VideoWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        Opened += (_, _) => ApplyFullscreenState();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DataContextProperty)
        {
            ApplyFullscreenState();
        }
    }

    private void ApplyFullscreenState()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        WindowState = viewModel.RemoteVideoFullscreen ? WindowState.FullScreen : WindowState.Normal;
    }
}