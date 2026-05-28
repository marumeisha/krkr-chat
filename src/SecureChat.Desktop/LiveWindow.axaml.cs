using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecureChat.Desktop.ViewModels;

namespace SecureChat.Desktop;

public partial class LiveWindow : Window
{
    public LiveWindow()
    {
        InitializeComponent();
    }

    public LiveWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}