using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Files;

/// <summary>A .dnk file could not be read (damaged, not a DoodleNook project, or from a newer DoodleNook).</summary>
public sealed class DnkFormatException(string message) : Exception(message);

/// <summary>
/// DoodleNook's native layered project format, ".dnk".
///
/// A .dnk file is a ZIP archive (open it with any unzip tool to look inside):
/// <list type="bullet">
/// <item><c>mimetype</c> — "application/x-doodlenook" (stored first, uncompressed).</item>
/// <item><c>document.json</c> — format version, canvas size, paper, palette and the layer list (bottom to top) with
/// names, visibility, opacity, blend mode, locks, clipping and colour labels.</item>
/// <item><c>layers/layer-NNN.png</c> — each layer's pixels, lossless 8-bit RGBA.</item>
/// <item><c>thumbnail.png</c> — a small flattened preview.</item>
/// </list>
/// Files are written to a temporary file first and then moved into place, so a failed save never damages the
/// previous version. Files from a newer format version are refused with a clear message instead of being misread.
/// </summary>
public static class DnkFile
{
    public const string Extension = ".dnk";
    public const string MimeType = "application/x-doodlenook";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Save(Document doc, string path, string? appVersion = null)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? ".";
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Write(doc, stream, appVersion);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static void Write(Document doc, Stream stream, string? appVersion = null)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mime.Open())) w.Write(MimeType);

        var manifest = new Manifest
        {
            Format = "DoodleNook",
            Version = CurrentVersion,
            CreatedWith = appVersion is null ? "DoodleNook" : $"DoodleNook {appVersion}",
            Saved = DateTimeOffset.Now,
            Width = doc.Width,
            Height = doc.Height,
            Paper = new PaperInfo { Color = doc.Paper?.ToHex(), Visible = doc.PaperVisible },
            ActiveLayer = doc.ActiveIndex,
            Palette = new PaletteInfo { Name = doc.Palette.Name, Colors = doc.Palette.Colors.Select(c => c.ToHex()).ToList() },
        };
        // Layers are independent: compress them on all cores, then write them in order.
        var encoded = new byte[doc.Layers.Count][];
        Parallel.For(0, doc.Layers.Count, i => encoded[i] = PngCodec.Encode(doc.Layers[i].Image));
        for (int i = 0; i < doc.Layers.Count; i++)
        {
            var l = doc.Layers[i];
            string file = $"layers/layer-{i + 1:000}.png";
            manifest.Layers.Add(new LayerInfo
            {
                Name = l.Name,
                File = file,
                Visible = l.Visible,
                Opacity = Math.Round(l.Opacity, 4),
                Blend = l.Blend,
                Locked = l.Locked,
                AlphaLocked = l.AlphaLocked,
                ClipToBelow = l.ClipToBelow,
                Label = l.Label,
            });
            var entry = zip.CreateEntry(file, CompressionLevel.NoCompression); // PNG is already compressed
            using var es = entry.Open();
            es.Write(encoded[i]);
        }

        var doc_ = zip.CreateEntry("document.json", CompressionLevel.Optimal);
        using (var js = doc_.Open()) JsonSerializer.Serialize(js, manifest, Json);

        var thumb = zip.CreateEntry("thumbnail.png", CompressionLevel.NoCompression);
        using (var ts = thumb.Open()) ts.Write(PngCodec.Encode(Thumbnail(doc, 256)));
    }

    public static Document Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static Document Read(Stream stream)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new DnkFormatException("This file isn't a DoodleNook project (or it is damaged).");
        }
        using (zip)
        {
            var manifestEntry = zip.GetEntry("document.json") ?? throw new DnkFormatException("This file isn't a DoodleNook project: document.json is missing.");
            Manifest manifest;
            try
            {
                using var ms = manifestEntry.Open();
                manifest = JsonSerializer.Deserialize<Manifest>(ms, Json) ?? throw new DnkFormatException("The project description is empty.");
            }
            catch (JsonException ex)
            {
                throw new DnkFormatException("The project description is damaged: " + ex.Message);
            }
            if (manifest.Format != "DoodleNook") throw new DnkFormatException("This file isn't a DoodleNook project.");
            if (manifest.Version > CurrentVersion)
                throw new DnkFormatException($"This project was saved by a newer DoodleNook (format version {manifest.Version}). Please update DoodleNook to open it.");
            if (manifest.Version < 1) throw new DnkFormatException("Unknown project format version.");
            if (!RasterImage.IsSupportedSize(manifest.Width, manifest.Height)) throw new DnkFormatException("The canvas size in this project is not supported.");

            // Read the compressed layers in order (ZIP streams are sequential), then decode them on all cores.
            var raw = new byte[manifest.Layers.Count][];
            for (int i = 0; i < raw.Length; i++)
            {
                var info = manifest.Layers[i];
                var entry = zip.GetEntry(info.File ?? "") ?? throw new DnkFormatException($"Layer \"{info.Name}\" is missing from the project.");
                using var es = entry.Open();
                using var buffer = new MemoryStream();
                es.CopyTo(buffer);
                raw[i] = buffer.ToArray();
            }
            var images = new RasterImage?[raw.Length];
            var errors = new string?[raw.Length];
            Parallel.For(0, raw.Length, i =>
            {
                try
                {
                    images[i] = PngCodec.Decode(raw[i]);
                }
                catch (InvalidDataException ex)
                {
                    errors[i] = ex.Message;
                }
            });

            var layers = new List<(LayerProperties, RasterImage)>();
            for (int i = 0; i < raw.Length; i++)
            {
                var info = manifest.Layers[i];
                if (errors[i] is { } error) throw new DnkFormatException($"Layer \"{info.Name}\" is damaged: {error}");
                var image = images[i]!;
                if (image.Width != manifest.Width || image.Height != manifest.Height)
                    throw new DnkFormatException($"Layer \"{info.Name}\" has the wrong size.");
                layers.Add((new LayerProperties(
                    string.IsNullOrWhiteSpace(info.Name) ? "Layer" : info.Name!,
                    info.Visible,
                    double.IsFinite(info.Opacity) ? Math.Clamp(info.Opacity, 0, 1) : 1,
                    Enum.IsDefined(info.Blend) ? info.Blend : BlendMode.Normal,
                    info.Locked,
                    info.AlphaLocked,
                    info.ClipToBelow,
                    Enum.IsDefined(info.Label) ? info.Label : LayerLabel.None), image));
            }
            if (layers.Count == 0) throw new DnkFormatException("The project has no layers.");

            ColorRgb? paper = manifest.Paper?.Color is { } hex && ColorRgb.TryParse(hex, out var pc) ? pc : null;
            var doc = new Document(manifest.Width, manifest.Height, paper, manifest.Paper?.Visible ?? true, layers, manifest.ActiveLayer);
            if (manifest.Palette is { Colors.Count: > 0 } pal)
                doc.Palette = new Palette(pal.Name ?? "Palette", pal.Colors.Select(s => ColorRgb.TryParse(s, out var c) ? (ColorRgb?)c : null).OfType<ColorRgb>());
            return doc;
        }
    }

    /// <summary>A flattened preview no larger than <paramref name="maxSize"/> on its longest side.</summary>
    public static RasterImage Thumbnail(Document doc, int maxSize)
    {
        var flat = doc.Flatten();
        double s = Math.Min(1.0, (double)maxSize / Math.Max(doc.Width, doc.Height));
        int w = Math.Max(1, (int)Math.Round(doc.Width * s)), h = Math.Max(1, (int)Math.Round(doc.Height * s));
        return Resampler.Resize(flat, w, h, Interpolation.Bilinear);
    }

    private sealed class Manifest
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public string? CreatedWith { get; set; }
        public DateTimeOffset? Saved { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public PaperInfo? Paper { get; set; }
        public int ActiveLayer { get; set; } = -1;
        public List<LayerInfo> Layers { get; set; } = new();
        public PaletteInfo? Palette { get; set; }
    }

    private sealed class PaperInfo
    {
        public string? Color { get; set; }
        public bool Visible { get; set; } = true;
    }

    private sealed class LayerInfo
    {
        public string? Name { get; set; }
        public string? File { get; set; }
        public bool Visible { get; set; } = true;
        public double Opacity { get; set; } = 1;
        public BlendMode Blend { get; set; }
        public bool Locked { get; set; }
        public bool AlphaLocked { get; set; }
        public bool ClipToBelow { get; set; }
        public LayerLabel Label { get; set; }
    }

    private sealed class PaletteInfo
    {
        public string? Name { get; set; }
        public List<string> Colors { get; set; } = new();
    }
}
