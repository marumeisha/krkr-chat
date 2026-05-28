using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecureChat.Desktop.ViewModels;

namespace SecureChat.Desktop;

public partial class MessageWindow : Window
{
    public MessageWindow()
    {
        InitializeComponent();
    }

    public MessageWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}