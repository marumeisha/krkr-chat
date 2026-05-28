using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecureChat.Desktop.ViewModels;

namespace SecureChat.Desktop;

public partial class LiveAudienceWindow : Window
{
    public LiveAudienceWindow()
    {
        InitializeComponent();
    }

    public LiveAudienceWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}