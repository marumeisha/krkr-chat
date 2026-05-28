using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SecureChat.Desktop.Views;

public partial class SessionHeaderView : UserControl
{
    public SessionHeaderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}