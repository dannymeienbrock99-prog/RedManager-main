using System.Windows;

namespace CrazyBatto.RedManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                CrashLog.Write(args.ExceptionObject as Exception ?? new Exception("Unbekannter nicht behandelter Fehler."));
            }
            catch
            {
                // Never throw from the final crash handler.
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write(args.Exception);
            MessageBox.Show(
                $"RedManager hat einen Fehler festgestellt.\n\n{args.Exception.Message}\n\nEin Protokoll wurde unter %LOCALAPPDATA%\\Crazy_Batto\\RedManager gespeichert.",
                "Crazy_Batto RedManager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}

internal static class CrashLog
{
    public static void Write(Exception exception)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Crazy_Batto",
            "RedManager",
            "logs");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(path, $"{DateTimeOffset.UtcNow:O}\n{exception}");
    }
}
