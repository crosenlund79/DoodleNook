using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DoodleNook.App.Controls;
using DoodleNook.App.Shell;

namespace DoodleNook.App.Dialogs;

/// <summary>
/// Mission 012 — Help › Quick Start Guide: docs/QUICK-START.md and its screenshots, built into DoodleNook so the guide is
/// always there and always matches the documentation. It is shown inside DoodleNook (no browser, nothing online). Only
/// the simple Markdown the guide uses is understood: headings, paragraphs, lists, pictures, **bold** and `keys`.
/// </summary>
public static class QuickStartGuide
{
    private const string Prefix = "Guide/";
    public const double ContentWidth = 700;

    /// <summary>One piece of the guide: a heading (level 1 or 2), a paragraph, a list item or a picture.</summary>
    public sealed record Block(string Kind, string Text, int Level = 0)
    {
        public string Alt { get; init; } = "";
    }

    public static string? Markdown() => Resource("QUICK-START.md") is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    /// <summary>A file built into DoodleNook from docs/ (the guide or one of its pictures), or null.</summary>
    public static byte[]? Resource(string path)
    {
        using var stream = typeof(QuickStartGuide).Assembly.GetManifestResourceStream(Prefix + path.Replace('\\', '/'));
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static readonly Regex ImageLine = new(@"^!\[(?<alt>[^\]]*)\]\((?<path>[^)]+)\)$");

    public static List<Block> Parse(string markdown)
    {
        var blocks = new List<Block>();
        var paragraph = new StringBuilder();
        string? kind = null;
        void Flush()
        {
            if (paragraph.Length > 0 && kind is not null) blocks.Add(new Block(kind, paragraph.ToString().Trim()));
            paragraph.Clear();
            kind = null;
        }
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }
            if (trimmed.StartsWith('#'))
            {
                Flush();
                int level = trimmed.TakeWhile(c => c == '#').Count();
                blocks.Add(new Block("heading", trimmed[level..].Trim(), level));
            }
            else if (ImageLine.Match(trimmed) is { Success: true } image)
            {
                Flush();
                blocks.Add(new Block("image", image.Groups["path"].Value, 0) { Alt = image.Groups["alt"].Value });
            }
            else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                Flush();
                kind = "item";
                paragraph.Append(trimmed[2..]);
            }
            else
            {
                if (kind is null) kind = "paragraph";
                if (paragraph.Length > 0) paragraph.Append(' ');
                paragraph.Append(trimmed);
            }
        }
        Flush();
        return blocks;
    }

    /// <summary>The guide as controls (pictures from <paramref name="picture"/>; a missing one is left out).</summary>
    public static StackPanel Build(IEnumerable<Block> blocks, Func<string, Bitmap?> picture)
    {
        var panel = new StackPanel { Spacing = 8, Width = ContentWidth, Name = "QuickStartGuide" };
        foreach (var b in blocks)
        {
            switch (b.Kind)
            {
                case "heading":
                    var h = Inline(b.Text);
                    h.FontSize = b.Level == 1 ? 22 : 16;
                    h.FontWeight = FontWeight.SemiBold;
                    h.Margin = new Thickness(0, b.Level == 1 ? 0 : 14, 0, 0);
                    h.Classes.Add("guide-heading");
                    panel.Children.Add(h);
                    break;
                case "image":
                    if (picture(b.Text) is not { } bitmap) break;
                    var img = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        MaxWidth = Math.Min(ContentWidth, bitmap.PixelSize.Width),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Classes = { "guide-picture" },
                    };
                    ToolTip.SetTip(img, b.Alt);
                    panel.Children.Add(new Border { Child = img, CornerRadius = new CornerRadius(8), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left });
                    break;
                case "item":
                    var row = new DockPanel();
                    var dot = new TextBlock { Text = "•", Margin = new Thickness(6, 0, 8, 0), Foreground = new SolidColorBrush(DnColors.Accent) };
                    DockPanel.SetDock(dot, Dock.Left);
                    row.Children.Add(dot);
                    row.Children.Add(Inline(b.Text));
                    panel.Children.Add(row);
                    break;
                default:
                    panel.Children.Add(Inline(b.Text));
                    break;
            }
        }
        return panel;
    }

    private static readonly Regex Span = new(@"\*\*(?<bold>.+?)\*\*|`(?<key>[^`]+)`|\*(?<em>[^*]+)\*");

    /// <summary>Text with **bold**, `keys` and *emphasis*.</summary>
    private static TextBlock Inline(string text)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        int at = 0;
        foreach (Match m in Span.Matches(text))
        {
            if (m.Index > at) t.Inlines!.Add(new Run(text[at..m.Index]));
            if (m.Groups["bold"].Success) t.Inlines!.Add(new Run(m.Groups["bold"].Value) { FontWeight = FontWeight.SemiBold });
            else if (m.Groups["key"].Success)
                t.Inlines!.Add(new Run(m.Groups["key"].Value) { FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(DnColors.Accent) });
            else t.Inlines!.Add(new Run(m.Groups["em"].Value) { FontStyle = FontStyle.Italic });
            at = m.Index + m.Length;
        }
        if (at < text.Length) t.Inlines!.Add(new Run(text[at..]));
        return t;
    }

    public static async Task ShowAsync(MainWindow app)
    {
        if (Markdown() is not { } md)
        {
            await app.Dialogs.ShowMessageAsync("Quick Start Guide", "The guide isn't included in this copy of DoodleNook.");
            return;
        }
        var guide = Build(Parse(md), path => Resource(path) is { } bytes ? new Bitmap(new MemoryStream(bytes)) : null);
        var body = new ScrollViewer
        {
            Content = guide,
            MaxHeight = Math.Max(320, app.Bounds.Height - 170),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 14, 0),
        };
        await app.Dialogs.ShowAsync("Quick Start Guide", body, [("close", "Close", true)], "close", "close", ContentWidth + 70);
        foreach (var img in guide.GetLogicalDescendants().OfType<Image>()) (img.Source as IDisposable)?.Dispose();
    }
}
