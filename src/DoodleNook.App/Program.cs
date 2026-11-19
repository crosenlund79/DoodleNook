using Avalonia;

namespace DoodleNook.App;

internal static class Program
{
    /// <summary>Command-line options (all optional).</summary>
    internal static StartupOptions Options { get; private set; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        Options = StartupOptions.Parse(args);
        int code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return SmokeTest.ExitCode ?? code;
    }

    // Also used by the visual designer and the headless tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // Draw pop-overs inside the main window: consistent look, real shadows, no extra OS windows.
            .With(new Win32PlatformOptions { OverlayPopups = true })
            .With(new X11PlatformOptions { OverlayPopups = true })
            .LogToTrace();
}

internal sealed record StartupOptions
{
    /// <summary>A PNG to open at start (e.g. "Open with DoodleNook").</summary>
    public string? OpenPath { get; init; }

    /// <summary>When set, runs the scripted self-test, writes results there and exits (used by CI).</summary>
    public string? SmokeTestDirectory { get; init; }

    public static StartupOptions Parse(string[] args)
    {
        var o = new StartupOptions();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--smoke-test")
            {
                o = o with { SmokeTestDirectory = i + 1 < args.Length ? args[++i] : "smoke-test" };
            }
            else if (!args[i].StartsWith("--", StringComparison.Ordinal) && o.OpenPath is null)
            {
                o = o with { OpenPath = args[i] };
            }
        }
        return o;
    }
}
