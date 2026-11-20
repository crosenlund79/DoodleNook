using System.IO.Compression;
using System.Text.Json;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Library;

/// <summary>One picture on a reference board, at a place and size on the board (board pixels).</summary>
public sealed class ReferenceItem
{
    public ReferenceItem(string id, string name, RasterImage image, double x, double y, double width)
    {
        Id = id;
        Name = name;
        Image = image;
        X = x;
        Y = y;
        Width = width;
    }

    public string Id { get; }
    public string Name { get; set; }
    public RasterImage Image { get; }
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Shown width; the height keeps the picture's proportions.</summary>
    public double Width { get; set; }

    public double Height => Width * Image.Height / Image.Width;

    public bool Contains(double bx, double by) => bx >= X && by >= Y && bx < X + Width && by < Y + Height;
}

/// <summary>
/// A reference board: pictures to look at while drawing. They are never part of the artwork (never in a layer,
/// a save or an export). Saved as a <c>.dnkboard</c> file: a ZIP with <c>board.json</c> and DoodleNook's own copies of
/// the pictures (so moving the originals breaks nothing).
/// </summary>
public sealed class ReferenceBoard
{
    public const string Extension = ".dnkboard";
    public const int MaxPictureSide = 4096;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<ReferenceItem> Items { get; } = new();

    /// <summary>The board's background (a few calm choices in the UI).</summary>
    public ColorRgb Background { get; set; } = ColorRgb.Parse("#2B2522");

    /// <summary>The view: board point at the panel's top-left, and zoom.</summary>
    public double ViewX { get; set; }
    public double ViewY { get; set; }
    public double Zoom { get; set; } = 1;

    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Adds a picture to the right of the others (very large pictures are reduced to 4096 px).</summary>
    public ReferenceItem Add(RasterImage image, string name)
    {
        if (Math.Max(image.Width, image.Height) > MaxPictureSide)
        {
            double s = (double)MaxPictureSide / Math.Max(image.Width, image.Height);
            image = Operations.Resampler.Resize(image, Math.Max(1, (int)(image.Width * s)), Math.Max(1, (int)(image.Height * s)), Operations.Interpolation.Bilinear);
        }
        double x = Items.Count == 0 ? 0 : Items.Max(i => i.X + i.Width) + 24;
        double width = Math.Min(480, image.Width);
        var item = new ReferenceItem(CreativeLibrary.NewId(), string.IsNullOrWhiteSpace(name) ? "Reference" : name, image, x, 0, width);
        Items.Add(item);
        Changed?.Invoke();
        return item;
    }

    public bool Remove(string id)
    {
        if (Items.RemoveAll(i => i.Id == id) == 0) return false;
        Changed?.Invoke();
        return true;
    }

    public void BringToFront(ReferenceItem item)
    {
        if (!Items.Remove(item)) return;
        Items.Add(item);
        Changed?.Invoke();
    }

    /// <summary>The top-most picture at a board point.</summary>
    public ReferenceItem? ItemAt(double bx, double by) => Items.LastOrDefault(i => i.Contains(bx, by));

    /// <summary>The colour of the top-most picture at a board point (for the eyedropper), or null.</summary>
    public ColorRgb? ColorAt(double bx, double by)
    {
        var item = ItemAt(bx, by);
        if (item is null) return null;
        int px = Math.Clamp((int)((bx - item.X) / item.Width * item.Image.Width), 0, item.Image.Width - 1);
        int py = Math.Clamp((int)((by - item.Y) / item.Height * item.Image.Height), 0, item.Image.Height - 1);
        uint p = item.Image[px, py];
        if (Pixel.A(p) < 8) return null;
        var (r, g, b, _) = Pixel.ToStraight(p);
        return new ColorRgb(r, g, b);
    }

    public void Save(string path)
    {
        var file = new BoardFile
        {
            Format = "DoodleNook reference board",
            Version = 1,
            Background = Background.ToHex(),
            View = new ViewDto { X = ViewX, Y = ViewY, Zoom = Zoom },
            Items = Items.Select(i => new ItemDto { Id = i.Id, Name = i.Name, X = i.X, Y = i.Y, Width = i.Width }).ToList(),
        };
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var s = zip.CreateEntry("board.json").Open()) JsonSerializer.Serialize(s, file, Json);
            foreach (var i in Items)
            {
                using var e = zip.CreateEntry($"images/{i.Id}.png", CompressionLevel.Fastest).Open();
                e.Write(PngCodec.Encode(i.Image));
            }
        }
        AnimationExport.WriteAtomically(path, ms.ToArray());
    }

    /// <summary>Opens a board. Throws <see cref="InvalidDataException"/> for a damaged or foreign file.</summary>
    public static ReferenceBoard Load(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry("board.json") ?? throw new InvalidDataException("This is not a DoodleNook reference board.");
            BoardFile file;
            using (var s = entry.Open()) file = JsonSerializer.Deserialize<BoardFile>(s, Json) ?? throw new InvalidDataException("The board description is empty.");
            if (file.Version > 1) throw new InvalidDataException("This board was saved by a newer DoodleNook.");
            var board = new ReferenceBoard
            {
                Background = ColorRgb.TryParse(file.Background, out var bg) ? bg : ColorRgb.Parse("#2B2522"),
                ViewX = Finite(file.View?.X, 0),
                ViewY = Finite(file.View?.Y, 0),
                Zoom = Math.Clamp(Finite(file.View?.Zoom, 1), 0.05, 20),
            };
            foreach (var i in file.Items ?? [])
            {
                var pic = zip.GetEntry($"images/{i.Id}.png");
                if (pic is null || string.IsNullOrEmpty(i.Id)) continue; // a missing picture only loses that picture
                using var s = pic.Open();
                using var buffer = new MemoryStream();
                s.CopyTo(buffer);
                var img = PngCodec.Decode(buffer.ToArray());
                board.Items.Add(new ReferenceItem(i.Id, i.Name ?? "Reference", img, Finite(i.X, 0), Finite(i.Y, 0), Math.Clamp(Finite(i.Width, img.Width), 8, 20000)));
            }
            return board;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException($"The reference board could not be read ({ex.Message}).", ex);
        }
    }

    private static double Finite(double? v, double fallback) => v is { } d && double.IsFinite(d) ? d : fallback;

    private sealed class BoardFile
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public string? Background { get; set; }
        public ViewDto? View { get; set; }
        public List<ItemDto>? Items { get; set; }
    }

    private sealed class ViewDto
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Zoom { get; set; }
    }

    private sealed class ItemDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
    }
}
