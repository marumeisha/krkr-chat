using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SecureChat.Desktop.Views;

public partial class FooterStatusView : UserControl
{
    public FooterStatusView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}