using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using DoodleNook.App.Editor;
using DoodleNook.App.Shell;
using DoodleNook.Engine.Documents;

namespace DoodleNook.App.Animation;

/// <summary>
/// Mission 012: a layer's opacity straight from Animation Studio's timeline (click the percentage in a layer's header).
/// A slider for quick changes (one undo step per drag, previewed live) and a box for an exact percentage. Opacity belongs
/// to the layer, so it applies on every frame, in playback, onion skins and exports, and is saved in the .dnk.
/// </summary>
public sealed class LayerOpacityPanel : StackPanel
{
    public LayerOpacityPanel(EditorSession session, Layer layer)
    {
        Name = "LayerOpacityPanel";
        Width = 270;
        Spacing = 8;
        var doc = session.Document;
        Children.Add(UiKit.Heading(layer.Name));

        var box = new TextBox { Name = "LayerOpacityBox", Width = 56, Height = 30, HorizontalContentAlignment = HorizontalAlignment.Right };
        LayerProperties? dragStart = null;
        var (slider, refresh) = UiKit.Slider(null, 0, 1, () => layer.Opacity,
            v =>
            {
                dragStart ??= layer.Properties;
                doc.PreviewLayerProperties(layer, layer.Properties with { Opacity = Math.Round(v, 2) });
                box.Text = Percent(layer.Opacity);
            },
            v => $"{v * 100:0}%", trackWidth: 150,
            finished: () =>
            {
                if (dragStart is { } from) doc.CommitLayerProperties(layer, from, "Layer opacity");
                dragStart = null;
            },
            tip: "Layer opacity: how strongly this layer shows, on every frame");
        slider.Name = "LayerOpacitySlider";

        void Apply()
        {
            string text = (box.Text ?? "").Trim().TrimEnd('%').Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            {
                v = Math.Clamp(Math.Round(v), 0, 100) / 100;
                if (Math.Abs(v - layer.Opacity) > 0.001) session.SetLayerProperty(layer, p => p with { Opacity = v }, "Layer opacity");
            }
            box.Text = Percent(layer.Opacity);
            refresh();
        }
        box.Text = Percent(layer.Opacity);
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Apply();
            e.Handled = true;
        };
        box.LostFocus += (_, _) => Apply();

        var exact = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        exact.Children.Add(box);
        exact.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Classes = { "dim" } });
        Children.Add(UiKit.Row(8, slider, exact));
        Children.Add(new TextBlock
        {
            Text = "Applies to this layer on every frame. Hidden layers and onion skins follow it too.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12, Classes = { "dim" },
        });
    }

    private static string Percent(double opacity) => (Math.Clamp(opacity, 0, 1) * 100).ToString("0", CultureInfo.InvariantCulture);
}
