using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SecureChat.Desktop.Views;

public partial class LiveBroadcastPanelView : UserControl
{
    public LiveBroadcastPanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}