using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DoodleNook.App.Controls;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.App.Dialogs;

/// <summary>
/// Simple in-window dialogs (a dimmed backdrop with a card), styled like the rest of DoodleNook.
/// Only one dialog is shown at a time.
/// </summary>
public sealed class DialogHost
{
    private readonly Panel _layer;
    private readonly Border _card;
    private TaskCompletionSource<string?>? _pending;
    private string? _defaultResult;
    private string? _cancelResult;

    public DialogHost(Panel layer, Border card)
    {
        _layer = layer;
        _card = card;
        _layer.PointerPressed += (_, e) => e.Handled = true; // block the canvas while a dialog is open
    }

    public bool IsOpen => _pending is not null;

    /// <summary>Shows a dialog and returns the id of the button pressed (null if cancelled with Esc).</summary>
    public Task<string?> ShowAsync(string title, Control body, (string Id, string Label, bool Primary)[] buttons, string? defaultId, string? cancelId)
    {
        _pending?.TrySetResult(_cancelResult);
        var tcs = new TaskCompletionSource<string?>();
        _pending = tcs;
        _defaultResult = defaultId;
        _cancelResult = cancelId;

        var stack = new StackPanel { Spacing = 14, Width = 380 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        head.Children.Add(new BrandMark());
        head.Children.Add(new TextBlock { Text = title, FontFamily = (FontFamily)Application.Current!.FindResource("DnBrandFont")!, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(head);
        stack.Children.Add(body);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (id, label, primary) in buttons)
        {
            var b = Ui.Button(null, label);
            b.Padding = new Thickness(14, 0);
            if (primary)
            {
                b.Classes.Add("solid");
                b.Classes.Add("on");
            }
            else
            {
                b.Background = new SolidColorBrush(DnColors.Ui3);
            }
            b.Click += (_, _) => Close(id);
            row.Children.Add(b);
        }
        stack.Children.Add(row);
        _card.Child = stack;
        _layer.IsVisible = true;
        return tcs.Task;
    }

    public void Close(string? result)
    {
        var p = _pending;
        _pending = null;
        _layer.IsVisible = false;
        _card.Child = null;
        p?.TrySetResult(result);
    }

    /// <summary>Lets the dialog react to Enter/Esc. Returns true if the key was used.</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        if (!IsOpen) return false;
        if (e.Key == Key.Escape)
        {
            Close(_cancelResult);
            return true;
        }
        if (e.Key == Key.Enter && _defaultResult is not null)
        {
            Close(_defaultResult);
            return true;
        }
        return false;
    }

    public static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(DnColors.Text2), LineHeight = 19 };

    /// <summary>"Save changes to … ?" Returns "save", "discard" or null (cancel).</summary>
    public async Task<string?> AskSaveChangesAsync(string name, string beforeWhat)
    {
        var r = await ShowAsync($"Save “{name}”?", Text($"You have unsaved changes. Save them before {beforeWhat}?"),
            [("discard", "Don't save", false), ("cancel", "Cancel", false), ("save", "Save", true)], "save", "cancel");
        return r == "cancel" ? null : r;
    }

    public Task ShowMessageAsync(string title, string message) =>
        ShowAsync(title, Text(message), [("ok", "OK", true)], "ok", "ok");

    /// <summary>The New picture dialog. Returns the size and background, or null when cancelled.</summary>
    public async Task<(int Width, int Height, ColorRgb? Background)?> AskNewDocumentAsync(int width, int height, bool white)
    {
        var w = new TextBox { Text = width.ToString(), Width = 90 };
        var h = new TextBox { Text = height.ToString(), Width = 90 };
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#FF8A7A")), IsVisible = false, TextWrapping = TextWrapping.Wrap };

        var presets = new WrapPanel();
        foreach (var (label, pw, ph) in new[] { ("Screen 1920 × 1080", 1920, 1080), ("Large 2560 × 1440", 2560, 1440), ("Square 2048", 2048, 2048), ("A4 portrait 2480 × 3508", 2480, 3508) })
        {
            var b = Ui.Button(null, label);
            b.Height = 30;
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Background = new SolidColorBrush(DnColors.Ui3);
            b.Click += (_, _) =>
            {
                w.Text = pw.ToString();
                h.Text = ph.ToString();
            };
            presets.Children.Add(b);
        }

        bool useWhite = white;
        var whiteBtn = Ui.Button(null, "White");
        var clearBtn = Ui.Button(null, "Transparent");
        void Mark()
        {
            whiteBtn.Classes.Set("on", useWhite);
            clearBtn.Classes.Set("on", !useWhite);
        }
        whiteBtn.Click += (_, _) => { useWhite = true; Mark(); };
        clearBtn.Click += (_, _) => { useWhite = false; Mark(); };
        Mark();

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sizeRow.Children.Add(new TextBlock { Text = "Width", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center });
        sizeRow.Children.Add(w);
        sizeRow.Children.Add(new TextBlock { Text = "Height", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        sizeRow.Children.Add(h);
        sizeRow.Children.Add(new TextBlock { Text = "px", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center });

        var bgRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        bgRow.Children.Add(new TextBlock { Text = "Background", Classes = { "dim" }, VerticalAlignment = VerticalAlignment.Center, Width = 82 });
        bgRow.Children.Add(whiteBtn);
        bgRow.Children.Add(clearBtn);

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(presets);
        body.Children.Add(sizeRow);
        body.Children.Add(bgRow);
        body.Children.Add(error);

        while (true)
        {
            var r = await ShowAsync("New picture", body, [("cancel", "Cancel", false), ("create", "Create", true)], "create", "cancel");
            if (r != "create") return null;
            if (int.TryParse(w.Text, out int iw) && int.TryParse(h.Text, out int ih) && RasterImage.IsSupportedSize(iw, ih))
                return (iw, ih, useWhite ? ColorRgb.White : null);
            error.Text = $"Please enter a size from 1 to {RasterImage.MaxDimension} pixels per side (up to {RasterImage.MaxPixels / 1_000_000} megapixels).";
            error.IsVisible = true;
        }
    }
}
