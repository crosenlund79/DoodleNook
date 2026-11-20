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
/// <item><c>layers/layer-NNN.png</c> — each layer's pixels, lossless 8-bit RGBA (still pictures).</item>
/// <item><c>cels/cel-NNNN.png</c> — animation drawings, cropped to their painted area; <c>document.json</c> says where
/// each one sits and, per layer, which drawing is shown on every frame (version 2).</item>
/// <item><c>thumbnail.png</c> — a small flattened preview.</item>
/// </list>
/// Version 1 is used for still pictures (so earlier DoodleNook versions keep opening them); version 2 adds the
/// <c>animation</c> section; version 3 adds the palette lock (<c>palette.locked</c> and <c>palette.libraryId</c>) and is
/// written only for palette-locked pictures, so an older DoodleNook refuses them instead of silently dropping the lock. Files are written to a temporary file first and then moved into place, so a failed save
/// never damages the previous version. Files from a newer format version are refused with a clear message.
/// </summary>
public static class DnkFile
{
    public const string Extension = ".dnk";
    public const string MimeType = "application/x-doodlenook";

    /// <summary>The newest format this build reads and writes (3 = palette lock).</summary>
    public const int CurrentVersion = 3;

    /// <summary>Animations are written in version 2, which Mission 006 builds can read.</summary>
    public const int AnimationVersion = 2;

    /// <summary>Palette-locked pictures are written in version 3.</summary>
    public const int PaletteLockVersion = 3;

    /// <summary>Still pictures are written in version 1, which Mission 005 builds can read.</summary>
    public const int StillVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // ------------------------------------------------------------------ saving

    public static void Save(Document doc, string path, string? appVersion = null) => Save(Snapshot(doc, appVersion), path);

    public static void Write(Document doc, Stream stream, string? appVersion = null) => Write(Snapshot(doc, appVersion), stream);

    /// <summary>
    /// Copies everything a save needs (cropped pixels, settings) so the slow part — compressing and writing — can run
    /// on a background thread while the user keeps drawing. Cheap: only painted areas are copied.
    /// </summary>
    public static DnkSnapshot Snapshot(Document doc, string? appVersion = null)
    {
        var manifest = new Manifest
        {
            Format = "DoodleNook",
            Version = doc.PaletteLocked ? PaletteLockVersion : doc.IsAnimated ? AnimationVersion : StillVersion,
            CreatedWith = appVersion is null ? "DoodleNook" : $"DoodleNook {appVersion}",
            Width = doc.Width,
            Height = doc.Height,
            Paper = new PaperInfo { Color = doc.Paper?.ToHex(), Visible = doc.PaperVisible },
            ActiveLayer = doc.ActiveIndex,
            Palette = new PaletteInfo
            {
                Name = doc.Palette.Name,
                Colors = doc.Palette.Colors.Select(c => c.ToHex()).ToList(),
                Locked = doc.PaletteLocked ? true : null,
                LibraryId = doc.PaletteLocked ? doc.Palette.LibraryId : null,
            },
        };
        var images = new List<SnapshotImage>();
        foreach (var l in doc.Layers)
            manifest.Layers.Add(new LayerInfo
            {
                Name = l.Name,
                Visible = l.Visible,
                Opacity = Math.Round(l.Opacity, 4),
                Blend = l.Blend,
                Locked = l.Locked,
                AlphaLocked = l.AlphaLocked,
                ClipToBelow = l.ClipToBelow,
                Label = l.Label,
            });

        if (doc.Animation is not { } a)
        {
            for (int i = 0; i < doc.Layers.Count; i++)
            {
                var l = doc.Layers[i];
                string file = $"layers/layer-{i + 1:000}.png";
                manifest.Layers[i].File = file;
                var bounds = l.ContentBounds.Intersect(doc.Bounds);
                images.Add(new SnapshotImage(file, bounds.IsEmpty ? null : l.Image.Crop(bounds), bounds, FullSize: true));
            }
        }
        else
        {
            var numbers = new Dictionary<Cel, int>();
            var anim = new AnimationInfo
            {
                Fps = a.Fps,
                Loop = a.Loop,
                PingPong = a.PingPong,
                CurrentFrame = a.CurrentFrame,
                Durations = Enumerable.Range(0, a.FrameCount).Select(a.DurationOverride).ToList(),
            };
            foreach (var l in doc.Layers)
            {
                var track = new List<int?>();
                foreach (var cel in l.Track)
                {
                    if (cel is null)
                    {
                        track.Add(null);
                        continue;
                    }
                    if (!numbers.TryGetValue(cel, out int n))
                    {
                        n = numbers[cel] = numbers.Count + 1;
                        var bounds = cel.ContentBounds.Intersect(doc.Bounds);
                        string? file = bounds.IsEmpty ? null : $"cels/cel-{n:0000}.png";
                        anim.Cels.Add(new CelInfo { Id = n, File = file, X = bounds.X, Y = bounds.Y });
                        if (file is not null) images.Add(new SnapshotImage(file, cel.Image.Crop(bounds), bounds, FullSize: false));
                    }
                    track.Add(n);
                }
                anim.Tracks.Add(track);
            }
            manifest.Animation = anim;
        }
        var preview = doc.IsAnimated ? doc.RenderFrame(doc.CurrentFrame) : doc.Flatten();
        return new DnkSnapshot(manifest, images, preview, doc.History.CurrentStateId);
    }

    /// <summary>Writes a snapshot to a file safely (temporary file, then replace). Thread-safe.</summary>
    public static void Save(DnkSnapshot snapshot, string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? ".";
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Write(snapshot, stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static void Write(DnkSnapshot snapshot, Stream stream)
    {
        var manifest = snapshot.Manifest;
        manifest.Saved = DateTimeOffset.Now;
        int w = manifest.Width, h = manifest.Height;
        // Images are independent: compress them on all cores, then write them in order.
        var encoded = new byte[snapshot.Images.Count][];
        Parallel.For(0, snapshot.Images.Count, i =>
        {
            var img = snapshot.Images[i];
            if (!img.FullSize)
            {
                encoded[i] = PngCodec.Encode(img.Pixels!);
                return;
            }
            var full = img.Pixels is not null && img.Bounds == new PixelRect(0, 0, w, h) ? img.Pixels : new RasterImage(w, h);
            if (!ReferenceEquals(full, img.Pixels) && img.Pixels is not null) full.Paste(img.Pixels, img.Bounds.X, img.Bounds.Y);
            encoded[i] = PngCodec.Encode(full);
        });

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var mw = new StreamWriter(mime.Open())) mw.Write(MimeType);
        for (int i = 0; i < snapshot.Images.Count; i++)
        {
            var entry = zip.CreateEntry(snapshot.Images[i].File, CompressionLevel.NoCompression); // PNG is already compressed
            using var es = entry.Open();
            es.Write(encoded[i]);
        }
        var docEntry = zip.CreateEntry("document.json", CompressionLevel.Optimal);
        using (var js = docEntry.Open()) JsonSerializer.Serialize(js, manifest, Json);

        var thumb = zip.CreateEntry("thumbnail.png", CompressionLevel.NoCompression);
        using (var ts = thumb.Open()) ts.Write(PngCodec.Encode(ThumbnailOf(snapshot.Preview, 256)));
    }

    // ------------------------------------------------------------------ opening

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
            if (manifest.Layers.Count == 0) throw new DnkFormatException("The project has no layers.");

            var props = manifest.Layers.Select(info => new LayerProperties(
                string.IsNullOrWhiteSpace(info.Name) ? "Layer" : info.Name!,
                info.Visible,
                double.IsFinite(info.Opacity) ? Math.Clamp(info.Opacity, 0, 1) : 1,
                Enum.IsDefined(info.Blend) ? info.Blend : BlendMode.Normal,
                info.Locked,
                info.AlphaLocked,
                info.ClipToBelow,
                Enum.IsDefined(info.Label) ? info.Label : LayerLabel.None)).ToList();
            ColorRgb? paper = manifest.Paper?.Color is { } hex && ColorRgb.TryParse(hex, out var pc) ? pc : null;

            Document doc;
            if (manifest.Version >= 2 && manifest.Animation is { } anim)
                doc = ReadAnimation(zip, manifest, anim, props, paper);
            else
            {
                var files = manifest.Layers.Select(l => (l.File ?? "", $"Layer \"{l.Name}\"")).ToList();
                var images = DecodeAll(zip, files);
                var layers = new List<(LayerProperties, RasterImage)>();
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i].Width != manifest.Width || images[i].Height != manifest.Height)
                        throw new DnkFormatException($"Layer \"{props[i].Name}\" has the wrong size.");
                    layers.Add((props[i], images[i]));
                }
                doc = new Document(manifest.Width, manifest.Height, paper, manifest.Paper?.Visible ?? true, layers, manifest.ActiveLayer);
            }
            if (manifest.Palette is { Colors.Count: > 0 } pal)
            {
                var palette = new Palette(pal.Name ?? "Palette", pal.Colors.Select(s => ColorRgb.TryParse(s, out var c) ? (ColorRgb?)c : null).OfType<ColorRgb>())
                {
                    LibraryId = pal.LibraryId,
                };
                if (manifest.Version >= PaletteLockVersion && pal.Locked == true && palette.Colors.Count > 0) doc.SetPaletteInternal(palette, locked: true);
                else doc.Palette = palette;
            }
            return doc;
        }
    }

    private static Document ReadAnimation(ZipArchive zip, Manifest manifest, AnimationInfo anim, List<LayerProperties> props, ColorRgb? paper)
    {
        int frames = anim.Durations.Count;
        if (frames < 1) throw new DnkFormatException("The animation has no frames.");
        if (anim.Tracks.Count != props.Count) throw new DnkFormatException("The animation's layers don't match its tracks (the file is damaged).");
        var withFiles = anim.Cels.Where(c => c.File is not null).ToList();
        var decoded = DecodeAll(zip, withFiles.Select(c => (c.File!, $"Drawing {c.Id}")).ToList());
        var cels = new Dictionary<int, Cel>();
        for (int i = 0; i < withFiles.Count; i++)
        {
            var info = withFiles[i];
            var part = decoded[i];
            var bounds = new PixelRect(info.X, info.Y, part.Width, part.Height);
            if (!new PixelRect(0, 0, manifest.Width, manifest.Height).Contains(bounds))
                throw new DnkFormatException($"Drawing {info.Id} lies outside the canvas (the file is damaged).");
            var full = new RasterImage(manifest.Width, manifest.Height);
            full.Paste(part, info.X, info.Y);
            cels[info.Id] = new Cel(full, bounds);
        }
        foreach (var info in anim.Cels.Where(c => c.File is null))
            cels[info.Id] = new Cel(new RasterImage(manifest.Width, manifest.Height), PixelRect.Empty);

        var tracks = new List<List<Cel?>>();
        foreach (var t in anim.Tracks)
        {
            if (t.Count != frames) throw new DnkFormatException("A layer's frames don't match the animation length (the file is damaged).");
            tracks.Add(t.Select(id => id is null ? null : cels.TryGetValue(id.Value, out var c) ? c
                : throw new DnkFormatException($"Drawing {id} is missing from the project.")).ToList());
        }
        return Document.CreateAnimated(manifest.Width, manifest.Height, paper, manifest.Paper?.Visible ?? true, props, tracks,
            anim.Fps, anim.Loop, anim.PingPong, anim.Durations, anim.CurrentFrame, manifest.ActiveLayer);
    }

    /// <summary>Reads entries in order (ZIP streams are sequential), then decodes them on all cores.</summary>
    private static RasterImage[] DecodeAll(ZipArchive zip, IReadOnlyList<(string File, string What)> entries)
    {
        var raw = new byte[entries.Count][];
        for (int i = 0; i < raw.Length; i++)
        {
            var entry = zip.GetEntry(entries[i].File) ?? throw new DnkFormatException($"{entries[i].What} is missing from the project.");
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
        for (int i = 0; i < raw.Length; i++)
            if (errors[i] is { } error) throw new DnkFormatException($"{entries[i].What} is damaged: {error}");
        return images!;
    }

    /// <summary>A flattened preview no larger than <paramref name="maxSize"/> on its longest side.</summary>
    public static RasterImage Thumbnail(Document doc, int maxSize) => ThumbnailOf(doc.Flatten(), maxSize);

    private static RasterImage ThumbnailOf(RasterImage flat, int maxSize)
    {
        double s = Math.Min(1.0, (double)maxSize / Math.Max(flat.Width, flat.Height));
        int w = Math.Max(1, (int)Math.Round(flat.Width * s)), h = Math.Max(1, (int)Math.Round(flat.Height * s));
        return Resampler.Resize(flat, w, h, Interpolation.Bilinear);
    }

    internal sealed class Manifest
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
        public AnimationInfo? Animation { get; set; }
    }

    internal sealed class AnimationInfo
    {
        public double Fps { get; set; } = Documents.Animation.DefaultFps;
        public bool Loop { get; set; } = true;
        public bool PingPong { get; set; }
        public int CurrentFrame { get; set; }

        /// <summary>One entry per frame: its own duration in milliseconds, or null to follow the frame rate.</summary>
        public List<int?> Durations { get; set; } = new();
        public List<CelInfo> Cels { get; set; } = new();

        /// <summary>Per layer (bottom to top), the drawing id shown on each frame (null = empty).</summary>
        public List<List<int?>> Tracks { get; set; } = new();
    }

    internal sealed class CelInfo
    {
        public int Id { get; set; }
        public string? File { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    internal sealed class PaperInfo
    {
        public string? Color { get; set; }
        public bool Visible { get; set; } = true;
    }

    internal sealed class LayerInfo
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

    internal sealed class PaletteInfo
    {
        public string? Name { get; set; }
        public List<string> Colors { get; set; } = new();

        /// <summary>Version 3: the picture may only use these colours.</summary>
        public bool? Locked { get; set; }

        /// <summary>Version 3: the library palette the colours were taken from.</summary>
        public string? LibraryId { get; set; }
    }
}

/// <summary>Everything needed to write a .dnk file, copied from a document at one moment (see <see cref="DnkFile.Snapshot"/>).
/// Safe to write on another thread while the document keeps changing.</summary>
public sealed class DnkSnapshot
{
    internal DnkSnapshot(DnkFile.Manifest manifest, IReadOnlyList<SnapshotImage> images, RasterImage preview, long stateId)
    {
        Manifest = manifest;
        Images = images;
        Preview = preview;
        StateId = stateId;
    }

    internal DnkFile.Manifest Manifest { get; }
    internal IReadOnlyList<SnapshotImage> Images { get; }
    internal RasterImage Preview { get; }

    /// <summary>The document's history state when the snapshot was taken (pass it to <see cref="Document.MarkSaved(long)"/>).</summary>
    public long StateId { get; }

    public bool IsAnimation => Manifest.Animation is not null;
}

/// <summary>Pixels of one layer or cel, cropped to their painted area.</summary>
internal sealed record SnapshotImage(string File, RasterImage? Pixels, PixelRect Bounds, bool FullSize);
