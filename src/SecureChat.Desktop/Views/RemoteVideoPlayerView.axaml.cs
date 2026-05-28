using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia;
using SecureChat.Desktop.ViewModels;

namespace SecureChat.Desktop.Views;

public partial class RemoteVideoPlayerView : UserControl
{
    public RemoteVideoPlayerView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ToggleRemoteFullscreen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.RemoteVideoFullscreen = !viewModel.RemoteVideoFullscreen;
        if (TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        window.WindowState = viewModel.RemoteVideoFullscreen ? WindowState.FullScreen : WindowState.Normal;
    }

    private void ToggleLocalPreviewVisibility(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.LocalPreviewVisible = !viewModel.LocalPreviewVisible;
    }
}
