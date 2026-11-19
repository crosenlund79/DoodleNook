using Avalonia;
using Avalonia.Headless;
using DoodleNook.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace DoodleNook.App.Tests;

/// <summary>Runs the real DoodleNook application (styles, window, canvas) on Avalonia's headless platform with Skia
/// rendering, so tests can click, draw, press keys and capture real frames without a display.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = true });
}
