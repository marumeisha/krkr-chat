using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecureChat.Desktop.ViewModels;

namespace SecureChat.Desktop;

public partial class LiveHostWindow : Window
{
    public LiveHostWindow()
    {
        InitializeComponent();
    }

    public LiveHostWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}