using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SecureChat.Desktop.Views;

public partial class MessageListView : UserControl
{
    public MessageListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}