using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;

namespace DoodleNook.App.Dialogs;

/// <summary>Small dialogs used by the creative library: a name, a choice, a confirmation.</summary>
public static class LibraryDialogs
{
    /// <summary>Asks for a short text (a name or collection). Null when cancelled.</summary>
    public static async Task<string?> AskTextAsync(this DialogHost host, string title, string label, string initial, string ok = "Save",
        IReadOnlyList<string>? suggestions = null, string? note = null)
    {
        var box = new TextBox { Text = initial, Width = 260 };
        var body = new StackPanel { Spacing = 10 };
        if (note is not null) body.Children.Add(DialogHost.Text(note));
        body.Children.Add(UiKit.Row(8, new TextBlock { Text = label, Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Width = 80 }, box));
        if (suggestions is { Count: > 0 })
        {
            var wrap = new WrapPanel();
            foreach (var sgg in suggestions.Distinct().Take(12))
            {
                var b = UiKit.Button(null, sgg);
                b.Height = 26;
                b.Margin = new Thickness(0, 0, 6, 6);
                b.Background = new SolidColorBrush(DnColors.Ui3);
                var value = sgg;
                b.Click += (_, _) => box.Text = value;
                wrap.Children.Add(b);
            }
            body.Children.Add(wrap);
        }
        var r = await host.ShowAsync(title, body, [("cancel", "Cancel", false), ("ok", ok, true)], "ok", "cancel", 420);
        var text = box.Text?.Trim();
        return r == "ok" && !string.IsNullOrEmpty(text) ? text : null;
    }

    public static async Task<bool> ConfirmAsync(this DialogHost host, string title, string message, string ok)
    {
        var r = await host.ShowAsync(title, DialogHost.Text(message), [("cancel", "Cancel", false), ("ok", ok, true)], "cancel", "cancel", 420);
        return r == "ok";
    }
}
