using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DoodleNook.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (Program.Options.SmokeTestDirectory is { } dir)
                SmokeTest.Attach(window, dir);
            else if (Program.Options.OpenPath is { } path)
                window.Opened += async (_, _) => await window.OpenPathAsync(path);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
