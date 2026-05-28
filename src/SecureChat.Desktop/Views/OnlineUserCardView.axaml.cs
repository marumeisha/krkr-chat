using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SecureChat.Desktop.Views;

public partial class OnlineUserCardView : UserControl
{
    public OnlineUserCardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}