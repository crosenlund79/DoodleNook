using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DoodleNook.Engine.Documents;

namespace DoodleNook.App.Shell;

/// <summary>
/// Typing text for a text layer: a small box that floats under the text on the canvas. The text on the canvas follows
/// every key press. Ctrl+Enter, Esc or clicking elsewhere finishes; new text that was left empty disappears again.
/// A new text layer is one undo step ("Add text"); changing existing text is one more ("Edit text").
/// </summary>
public sealed class TextEditorOverlay : Border
{
    private readonly MainWindow _app;
    private readonly TextBox _box;
    private Layer? _layer;
    private TextContent? _from;
    private bool _isNew;
    private bool _finishing;

    public TextEditorOverlay(MainWindow app)
    {
        _app = app;
        Name = "TextEditor";
        Classes.Add("float");
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Padding = new Thickness(8);
        _box = new TextBox
        {
            Name = "TextEditorBox",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinWidth = 240,
            MaxWidth = 460,
            MinHeight = 34,
            MaxHeight = 200,
            Watermark = "Type your text",
        };
        _box.TextChanged += (_, _) => Preview();
        _box.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            // Focus can move to a pop-over of the text options; only finish when it really left the editor.
            if (!_box.IsFocused && !UiKit.AnyFlyoutOpen) Finish();
        });
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Finish();
                e.Handled = true;
            }
        };
        var hint = new TextBlock
        {
            Text = "Ctrl+Enter or Esc to finish · the options bar changes font, size and effects",
            Classes = { "dim" },
            FontSize = 11,
            Margin = new Thickness(2, 6, 2, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
        };
        Child = new StackPanel { Children = { _box, hint } };
        app.Canvas.View.Changed += Reposition;
    }

    public bool IsOpen => _layer is not null;

    /// <summary>Types into the editor as if from the keyboard (smoke test).</summary>
    internal void Type(string text)
    {
        if (IsOpen) _box.Text = text;
    }

    /// <summary>The layer being typed into, or null.</summary>
    public Layer? Layer => _layer;

    public void Open(Layer layer, bool isNew)
    {
        if (layer.Content is not TextContent text) return;
        if (IsOpen) Finish();
        _layer = layer;
        _from = text;
        _isNew = isNew;
        _box.Text = text.Text;
        IsVisible = true;
        Reposition();
        Dispatcher.UIThread.Post(() =>
        {
            _box.Focus();
            if (isNew) _box.CaretIndex = _box.Text?.Length ?? 0;
            else _box.SelectAll();
        });
    }

    private void Preview()
    {
        if (_layer is null || _finishing || _layer.Content is not TextContent current) return;
        var text = _box.Text ?? "";
        if (text == current.Text) return;
        _app.Session.Document.PreviewContent(_layer, current with { Text = text });
        Reposition();
    }

    /// <summary>Keeps the box just under the text (it follows zoom, panning and the text growing).</summary>
    private void Reposition()
    {
        if (_layer is null || _app.Session.Document.BoxOf(_layer) is not { } box) return;
        var (l, _, r, b) = box.Extent();
        var view = _app.Canvas.View;
        var corners = new[] { view.DocumentToView(l, b), view.DocumentToView(r, b) };
        double x = corners.Min(c => c.X), y = corners.Max(c => c.Y) + 10;
        var size = _app.Canvas.Bounds.Size;
        x = Math.Clamp(x, 8, Math.Max(8, size.Width - 300));
        y = Math.Clamp(y, 8, Math.Max(8, size.Height - 120));
        Margin = new Thickness(x, y, 0, 0);
    }

    /// <summary>Ends typing (keeps what was typed).</summary>
    public void Finish()
    {
        if (_layer is not { } layer || _finishing) return;
        Preview(); // the very last key press, even if its change event hasn't arrived yet
        _finishing = true;
        try
        {
            var doc = _app.Session.Document;
            var from = _from!;
            var text = _box.Text ?? "";
            _layer = null;
            IsVisible = false;
            if (!doc.Layers.Contains(layer) || layer.Content is not TextContent now) return;
            bool empty = string.IsNullOrWhiteSpace(text);
            if (_isNew)
            {
                if (empty)
                {
                    // Nothing typed: the new text layer goes away again (its "Add text" step is undone).
                    if (doc.History.NextUndoName == "Add text" && ReferenceEquals(doc.ActiveLayer, layer)) doc.Undo();
                    else doc.DeleteLayer(layer);
                }
                else
                {
                    // The "Add text" step holds this same layer, so it now adds the finished text; name it after the text.
                    doc.PreviewLayerProperties(layer, layer.Properties with { Name = Document.NameForText(now) });
                }
            }
            else if (empty)
            {
                doc.PreviewContent(layer, from);
                doc.DeleteLayer(layer);
            }
            else if (now != from) doc.CommitContent(layer, from, "Edit text");
        }
        finally
        {
            _finishing = false;
            _app.Canvas.Focus();
        }
    }
}
