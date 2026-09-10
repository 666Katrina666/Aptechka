using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Aptechka.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aptechka",
        "logs",
        "startup.log");

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            WriteStartupFailure("AppDomain", eventArgs.ExceptionObject);
        UnhandledException += (_, eventArgs) =>
            WriteStartupFailure("WinUI", eventArgs.Exception);

        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private static void WriteStartupFailure(string source, object? exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(StartupLogPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                StartupLogPath,
                $"{DateTimeOffset.Now:O} [{source}] {exception}{Environment.NewLine}");
        }
        catch
        {
            // Startup diagnostics must never replace the original exception.
        }
    }
}
