using Avalonia;
using System.Text;

namespace SecureChat.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                WriteStartupFailureLog(exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            WriteStartupFailureLog(eventArgs.Exception);
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteStartupFailureLog(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void WriteStartupFailureLog(Exception exception)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
            var payload = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.Now:O}] Desktop startup failure")
                .AppendLine(exception.ToString())
                .AppendLine();

            File.AppendAllText(logPath, payload.ToString(), Encoding.UTF8);
            Console.Error.WriteLine(exception);
        }
        catch
        {
            // Ignore logging failures during startup crash handling.
        }
    }
}