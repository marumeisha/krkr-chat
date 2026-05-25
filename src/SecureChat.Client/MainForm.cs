using System.Windows.Forms;

namespace SecureChat.Client;

public sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "SecureChat Client";
        Width = 900;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
    }
}
