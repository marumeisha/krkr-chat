using System;
using System.Windows.Forms;
using SecureChat.Client.Services;

namespace SecureChat.Client;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var options = ClientRuntimeOptions.Load();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(options.ApiBaseUri));
    }
}
