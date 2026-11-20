namespace DoodleNook.App.Files;

/// <summary>
/// A small local log of unexpected errors (%LOCALAPPDATA%\DoodleNook\Logs\errors.log), so a problem can be reported
/// precisely. Nothing is sent anywhere. The log is kept short: when it passes 1 MB the older half is dropped.
/// </summary>
public static class ErrorLog
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoodleNook", "Logs", "errors.log");

    public static void Write(Exception ex, string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
            {
                var text = File.ReadAllText(path);
                File.WriteAllText(path, text[(text.Length / 2)..]);
            }
            File.AppendAllText(path, $"---- {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} · DoodleNook {MainWindow.Version}{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never cause a second failure.
        }
    }
}
