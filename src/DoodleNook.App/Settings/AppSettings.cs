using System.Text.Json;
using DoodleNook.App.Editor;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Painting;

namespace DoodleNook.App.Settings;

/// <summary>
/// Remembers brush settings, colours and the window position between sessions, in
/// %APPDATA%\DoodleNook\settings.json. Purely local; a missing or broken file just means defaults.
/// </summary>
public sealed class AppSettings
{
    public BrushSettings? Brush { get; set; }
    public BrushSettings? Eraser { get; set; }
    public string? BrushPreset { get; set; }
    public PressureFeel PressureFeel { get; set; } = PressureFeel.Normal;
    public string? Foreground { get; set; }
    public string? Background { get; set; }
    public List<string>? RecentColors { get; set; }

    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DoodleNook", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception)
        {
            // Settings are a convenience; never let them break the app.
        }
    }

    public void ApplyTo(EditorSession session)
    {
        if (Brush is null || Eraser is null) return;
        var fg = ColorRgb.TryParse(Foreground, out var f) ? f : EditorSession.DefaultForeground;
        var bg = ColorRgb.TryParse(Background, out var b) ? b : EditorSession.DefaultBackground;
        var recent = RecentColors?.Select(s => ColorRgb.TryParse(s, out var c) ? (ColorRgb?)c : null).OfType<ColorRgb>();
        session.RestoreSettings(Brush, Eraser, BrushPreset ?? "pencil", PressureFeel, fg, bg, recent);
    }

    public void CaptureFrom(EditorSession session)
    {
        Brush = session.Brush;
        Eraser = session.Eraser;
        BrushPreset = session.BrushPresetId;
        PressureFeel = session.PressureFeel;
        Foreground = session.Foreground.ToHex();
        Background = session.Background.ToHex();
        RecentColors = session.RecentColors.Select(c => c.ToHex()).ToList();
    }
}
