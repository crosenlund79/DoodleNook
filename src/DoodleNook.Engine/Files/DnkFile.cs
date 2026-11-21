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
/// written only for palette-locked pictures, so an older DoodleNook refuses them instead of silently dropping the lock.
/// Version 4 adds layer groups (<c>parent</c>, <c>collapsed</c>), layer masks (<c>masks/mask-NNN.png</c>, white shows),
/// editable text (<c>text</c>), shapes (<c>shape</c>) and adjustment layers (<c>adjustment</c>), guides and the
/// perspective assistant; it is written only for pictures that use them. Text and shapes are stored as their description
/// (not pixels) and drawn again when opened; a missing font is replaced and named, and the layer keeps asking for it.
/// Version 5 (Mission 013) adds, per layer, the settings of a cycle from the Reference Animation Library
/// (<c>reference</c>: cycle, view, facing, size, anchor and timing) so it can be adjusted later; it is written only for
/// animations that have such a layer. The layer's drawings are stored like any other animation layer's.
/// Files are written to a temporary file first and then moved into place, so a failed save
/// never damages the previous version. Files from a newer format version are refused with a clear message.
/// </summary>
public static class DnkFile
{
    public const string Extension = ".dnk";
    public const string MimeType = "application/x-doodlenook";

    /// <summary>The newest format this build reads and writes (5 = reference cycles from the library).</summary>
    public const int CurrentVersion = 5;

    /// <summary>Animations with a Reference Animation Library layer are written in version 5.</summary>
    public const int ReferenceVersion = 5;

    /// <summary>Pictures with groups, masks, text, shapes, adjustment layers, guides or perspective are written in version 4.</summary>
    public const int DesignVersion = 4;

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
            Version = doc.Layers.Any(l => l.ReferenceCycle is not null) ? ReferenceVersion
                : doc.UsesDesignFeatures ? DesignVersion : doc.PaletteLocked ? PaletteLockVersion : doc.IsAnimated ? AnimationVersion : StillVersion,
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
        var full = new PixelRect(0, 0, doc.Width, doc.Height);
        for (int i = 0; i < doc.Layers.Count; i++)
        {
            var l = doc.Layers[i];
            var info = new LayerInfo
            {
                Name = l.Name,
                Visible = l.Visible,
                Opacity = Math.Round(l.Opacity, 4),
                Blend = l.Blend,
                Locked = l.Locked,
                AlphaLocked = l.AlphaLocked,
                ClipToBelow = l.ClipToBelow,
                Label = l.Label,
            };
            if (l.Kind != LayerKind.Raster) info.Kind = l.Kind;
            if (l.ParentId != 0 && doc.FindLayer(l.ParentId) is { } parent) info.Parent = doc.IndexOf(parent);
            if (l.Properties.Collapsed) info.Collapsed = true;
            if (!l.MaskEnabled) info.MaskEnabled = false;
            if (l.Mask is { } mask)
            {
                info.Mask = $"masks/mask-{i + 1:000}.png";
                images.Add(new SnapshotImage(info.Mask, mask.Clone(), full, FullSize: true));
            }
            if (l.ReferenceCycle is { } rc) info.Reference = ReferenceInfo.From(rc);
            switch (l.Content)
            {
                case TextContent t:
                    info.Text = TextInfo.From(t);
                    break;
                case ShapeContent sh:
                    info.Shape = ShapeInfo.From(sh);
                    break;
                case AdjustmentContent adj:
                    info.Adjustment = AdjustmentInfo.From(adj.Adjustment);
                    break;
            }
            manifest.Layers.Add(info);
        }
        if (doc.Guides.Count > 0) manifest.Guides = doc.Guides.Select(g => new GuideInfo { Vertical = g.Vertical, Position = Math.Round(g.Position, 3) }).ToList();
        if (doc.GuidesLocked) manifest.GuidesLocked = true;
        if (doc.Perspective is { Points: > 0 } p)
            manifest.Perspective = new PerspectiveInfo { Points = p.Points, V1X = p.V1X, V1Y = p.V1Y, V2X = p.V2X, V2Y = p.V2Y };

        if (doc.Animation is not { } a)
        {
            for (int i = 0; i < doc.Layers.Count; i++)
            {
                var l = doc.Layers[i];
                if (!l.IsPaintable) continue; // text and shapes are drawn from their description; groups have no pixels
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
        // The thumbnail's source. When nothing extra is shown over the picture (onion skins, floating pixels, previews), the
        // projection already is exactly that composite: copying it is far quicker than composing every layer again here on
        // the UI thread (Mission 011; the thumbnail comes out byte for byte the same).
        var preview = doc.ProjectionIsPicture ? doc.Projection.Clone()
            : doc.IsAnimated ? doc.RenderFrame(doc.CurrentFrame) : doc.Flatten();
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

            bool design = manifest.Version >= DesignVersion;
            var kinds = manifest.Layers.Select(info => design && info.Kind is { } k && Enum.IsDefined(k) ? k : LayerKind.Raster).ToList();
            var props = manifest.Layers.Select(info => new LayerProperties(
                string.IsNullOrWhiteSpace(info.Name) ? "Layer" : info.Name!,
                info.Visible,
                double.IsFinite(info.Opacity) ? Math.Clamp(info.Opacity, 0, 1) : 1,
                Enum.IsDefined(info.Blend) ? info.Blend : BlendMode.Normal,
                info.Locked,
                info.AlphaLocked,
                info.ClipToBelow,
                Enum.IsDefined(info.Label) ? info.Label : LayerLabel.None,
                Collapsed: design && info.Collapsed == true,
                MaskEnabled: !design || info.MaskEnabled != false)).ToList();
            ColorRgb? paper = manifest.Paper?.Color is { } hex && ColorRgb.TryParse(hex, out var pc) ? pc : null;

            Document doc;
            if (manifest.Version >= 2 && manifest.Animation is { } anim)
                doc = ReadAnimation(zip, manifest, anim, props, paper);
            else
            {
                var pixelLayers = Enumerable.Range(0, manifest.Layers.Count).Where(i => kinds[i] == LayerKind.Raster).ToList();
                var files = pixelLayers.Select(i => (manifest.Layers[i].File ?? "", $"Layer \"{manifest.Layers[i].Name}\"")).ToList();
                var decoded = DecodeAll(zip, files);
                var images = new RasterImage?[manifest.Layers.Count];
                for (int j = 0; j < pixelLayers.Count; j++) images[pixelLayers[j]] = decoded[j];
                var layers = new List<(LayerProperties, RasterImage)>();
                for (int i = 0; i < images.Length; i++)
                {
                    var image = images[i] ?? new RasterImage(manifest.Width, manifest.Height);
                    if (image.Width != manifest.Width || image.Height != manifest.Height)
                        throw new DnkFormatException($"Layer \"{props[i].Name}\" has the wrong size.");
                    layers.Add((props[i], image));
                }
                doc = new Document(manifest.Width, manifest.Height, paper, manifest.Paper?.Visible ?? true, layers, manifest.ActiveLayer);
            }
            if (design) ReadDesign(zip, manifest, doc, kinds);
            if (manifest.Version >= ReferenceVersion)
                for (int i = 0; i < manifest.Layers.Count && i < doc.Layers.Count; i++)
                    if (manifest.Layers[i].Reference?.ToSettings() is { } rc && doc.Layers[i].IsTrack) doc.Layers[i].ReferenceCycle = rc;
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
        bool design = manifest.Version >= DesignVersion;
        bool IsStatic(int i) => design && manifest.Layers[i].Kind is { } k && k != LayerKind.Raster;
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
        for (int i = 0; i < anim.Tracks.Count; i++)
        {
            var t = anim.Tracks[i];
            if (IsStatic(i))
            {
                tracks.Add(Enumerable.Repeat<Cel?>(null, frames).ToList()); // text, shapes, groups: replaced while opening
                continue;
            }
            if (t.Count != frames) throw new DnkFormatException("A layer's frames don't match the animation length (the file is damaged).");
            tracks.Add(t.Select(id => id is null ? null : cels.TryGetValue(id.Value, out var c) ? c
                : throw new DnkFormatException($"Drawing {id} is missing from the project.")).ToList());
        }
        return Document.CreateAnimated(manifest.Width, manifest.Height, paper, manifest.Paper?.Visible ?? true, props, tracks,
            anim.Fps, anim.Loop, anim.PingPong, anim.Durations, anim.CurrentFrame, manifest.ActiveLayer);
    }

    /// <summary>Version 4: layer kinds, contents, masks, groups, guides and perspective.</summary>
    private static void ReadDesign(ZipArchive zip, Manifest manifest, Document doc, IReadOnlyList<LayerKind> kinds)
    {
        var withMasks = Enumerable.Range(0, manifest.Layers.Count).Where(i => manifest.Layers[i].Mask is not null).ToList();
        var masks = DecodeAll(zip, withMasks.Select(i => (manifest.Layers[i].Mask!, $"The mask of \"{manifest.Layers[i].Name}\"")).ToList());
        for (int i = 0; i < manifest.Layers.Count; i++)
        {
            var info = manifest.Layers[i];
            int m = withMasks.IndexOf(i);
            LayerContent? content = kinds[i] switch
            {
                LayerKind.Text => info.Text?.ToContent() ?? new TextContent("Text"),
                LayerKind.Shape => info.Shape?.ToContent() ?? new ShapeContent(ShapeType.Rectangle, 0, 0, 64, 64, ColorRgb.Black),
                LayerKind.Adjustment => new AdjustmentContent(info.Adjustment?.ToAdjustment() ?? new BrightnessContrast(0, 0)),
                _ => null,
            };
            doc.PrepareLoadedLayer(i, kinds[i], content, m >= 0 ? masks[m] : null, info.Parent ?? -1);
        }
        var p = manifest.Perspective;
        var perspective = p is { Points: 1 or 2 } && double.IsFinite(p.V1X) && double.IsFinite(p.V1Y) && double.IsFinite(p.V2X) && double.IsFinite(p.V2Y)
            ? new PerspectiveGuide(p.Points, p.V1X, p.V1Y, p.V2X, p.V2Y) : PerspectiveGuide.Off;
        doc.FinishLoad((manifest.Guides ?? []).Select(g => new Guide(g.Vertical, g.Position)), manifest.GuidesLocked == true, perspective);
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

        /// <summary>Version 4: guide lines (never exported).</summary>
        public List<GuideInfo>? Guides { get; set; }
        public bool? GuidesLocked { get; set; }

        /// <summary>Version 4: the perspective assistant.</summary>
        public PerspectiveInfo? Perspective { get; set; }
    }

    internal sealed class GuideInfo
    {
        public bool Vertical { get; set; }
        public double Position { get; set; }
    }

    internal sealed class PerspectiveInfo
    {
        public int Points { get; set; }
        public double V1X { get; set; }
        public double V1Y { get; set; }
        public double V2X { get; set; }
        public double V2Y { get; set; }
    }

    private static double Finite(double v, double fallback, double min = double.MinValue, double max = double.MaxValue) =>
        double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

    private static ColorRgb Colour(string? hex, ColorRgb fallback) => hex is not null && ColorRgb.TryParse(hex, out var c) ? c : fallback;

    internal sealed class OutlineInfo
    {
        public string? Color { get; set; }
        public double Width { get; set; } = 2;
    }

    internal sealed class ShadowInfo
    {
        public string? Color { get; set; }
        public double OffsetX { get; set; } = 3;
        public double OffsetY { get; set; } = 4;
        public double Blur { get; set; } = 4;
        public double Opacity { get; set; } = 0.45;

        public static ShadowInfo? From(ShadowEffect? s) => s is null ? null
            : new() { Color = s.Color.ToHex(), OffsetX = s.OffsetX, OffsetY = s.OffsetY, Blur = s.Blur, Opacity = s.Opacity };

        public ShadowEffect ToEffect() => new(Colour(Color, ColorRgb.Black), Finite(OffsetX, 3, -1000, 1000), Finite(OffsetY, 4, -1000, 1000),
            Finite(Blur, 4, 0, 64), Finite(Opacity, 0.45, 0, 1));
    }

    internal sealed class TextInfo
    {
        public string? Text { get; set; }
        public string? Font { get; set; }
        public double Size { get; set; } = 48;
        public string? Color { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public TextAlign Align { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Rotation { get; set; }
        public double LetterSpacing { get; set; }
        public double LineSpacing { get; set; } = 1.2;
        public double BoxWidth { get; set; }
        public OutlineInfo? Outline { get; set; }
        public ShadowInfo? Shadow { get; set; }

        public static TextInfo From(TextContent t) => new()
        {
            Text = t.Text, Font = t.FontFamily, Size = t.Size, Color = t.Color.ToHex(), Bold = t.Bold, Italic = t.Italic, Align = t.Align,
            X = t.X, Y = t.Y, Rotation = t.Rotation, LetterSpacing = t.LetterSpacing, LineSpacing = t.LineSpacing, BoxWidth = t.BoxWidth,
            Outline = t.Outline is { } o ? new OutlineInfo { Color = o.Color.ToHex(), Width = o.Width } : null,
            Shadow = ShadowInfo.From(t.Shadow),
        };

        public TextContent ToContent() => new(
            Text ?? "", string.IsNullOrWhiteSpace(Font) ? "Segoe UI" : Font!, Finite(Size, 48, 1, 2000), Colour(Color, ColorRgb.Black), Bold, Italic,
            Enum.IsDefined(Align) ? Align : TextAlign.Left, Finite(X, 0), Finite(Y, 0), Finite(Rotation, 0, -360, 360),
            Finite(LetterSpacing, 0, -200, 1000), Finite(LineSpacing, 1.2, 0.5, 5), Finite(BoxWidth, 0, 0, 100_000),
            Outline is { } o ? new OutlineEffect(Colour(o.Color, ColorRgb.White), Finite(o.Width, 2, 0, 200)) : null,
            Shadow?.ToEffect());
    }

    internal sealed class ShapeInfo
    {
        public ShapeType Kind { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string? Fill { get; set; }
        public string? Stroke { get; set; }
        public double StrokeWidth { get; set; } = 4;
        public double Rotation { get; set; }
        public double CornerRadius { get; set; } = 16;
        public int Sides { get; set; } = 6;
        public ShadowInfo? Shadow { get; set; }

        public static ShapeInfo From(ShapeContent s) => new()
        {
            Kind = s.Kind, X = s.X, Y = s.Y, Width = s.Width, Height = s.Height, Fill = s.Fill?.ToHex(), Stroke = s.Stroke?.ToHex(),
            StrokeWidth = s.StrokeWidth, Rotation = s.Rotation, CornerRadius = s.CornerRadius, Sides = s.Sides, Shadow = ShadowInfo.From(s.Shadow),
        };

        public ShapeContent ToContent() => new(
            Enum.IsDefined(Kind) ? Kind : ShapeType.Rectangle, Finite(X, 0), Finite(Y, 0), Finite(Width, 64, -100_000, 100_000),
            Finite(Height, 64, -100_000, 100_000), Fill is { } f && ColorRgb.TryParse(f, out var fc) ? fc : null,
            Stroke is { } st && ColorRgb.TryParse(st, out var sc) ? sc : null, Finite(StrokeWidth, 4, 0, 1000), Finite(Rotation, 0, -360, 360),
            Finite(CornerRadius, 16, 0, 100_000), Math.Clamp(Sides, 3, 64), Shadow?.ToEffect());
    }

    internal sealed class AdjustmentInfo
    {
        public string? Type { get; set; }
        public Dictionary<string, double> Values { get; set; } = new();

        public static AdjustmentInfo From(Adjustment a) => a switch
        {
            BrightnessContrast b => new() { Type = "brightnessContrast", Values = { ["brightness"] = b.Brightness, ["contrast"] = b.Contrast } },
            HueSaturation h => new() { Type = "hueSaturation", Values = { ["hue"] = h.Hue, ["saturation"] = h.Saturation, ["lightness"] = h.Lightness } },
            Levels l => new()
            {
                Type = "levels",
                Values = { ["inBlack"] = l.InBlack, ["inWhite"] = l.InWhite, ["gamma"] = l.Gamma, ["outBlack"] = l.OutBlack, ["outWhite"] = l.OutWhite },
            },
            ColourBalance c => new() { Type = "colourBalance", Values = { ["cyanRed"] = c.CyanRed, ["magentaGreen"] = c.MagentaGreen, ["yellowBlue"] = c.YellowBlue } },
            _ => new() { Type = "brightnessContrast" },
        };

        public Adjustment ToAdjustment()
        {
            double V(string key, double fallback, double min, double max) => Values.TryGetValue(key, out var v) ? Finite(v, fallback, min, max) : fallback;
            return Type switch
            {
                "hueSaturation" => new HueSaturation(V("hue", 0, -180, 180), V("saturation", 0, -100, 100), V("lightness", 0, -100, 100)),
                "levels" => new Levels(V("inBlack", 0, 0, 254), V("inWhite", 255, 1, 255), V("gamma", 1, 0.1, 10), V("outBlack", 0, 0, 255), V("outWhite", 255, 0, 255)),
                "colourBalance" => new ColourBalance(V("cyanRed", 0, -100, 100), V("magentaGreen", 0, -100, 100), V("yellowBlue", 0, -100, 100)),
                _ => new BrightnessContrast(V("brightness", 0, -100, 100), V("contrast", 0, -100, 100)),
            };
        }
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

        // Version 4
        public LayerKind? Kind { get; set; }

        /// <summary>The index (in this list) of the group the layer is in; null at the top level.</summary>
        public int? Parent { get; set; }
        public bool? Collapsed { get; set; }
        public string? Mask { get; set; }
        public bool? MaskEnabled { get; set; }
        public TextInfo? Text { get; set; }
        public ShapeInfo? Shape { get; set; }
        public AdjustmentInfo? Adjustment { get; set; }

        // Version 5
        public ReferenceInfo? Reference { get; set; }
    }

    /// <summary>Version 5: a Reference Animation Library cycle's settings.</summary>
    internal sealed class ReferenceInfo
    {
        public string? Cycle { get; set; }
        public References.ReferenceView View { get; set; }
        public References.ReferenceFacing Facing { get; set; }
        public double Height { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public int Start { get; set; }
        public int Hold { get; set; } = 1;
        public bool Repeat { get; set; } = true;
        public bool Extend { get; set; } = true;
        public bool Marker { get; set; } = true;
        public bool HardEdges { get; set; }

        public static ReferenceInfo From(References.ReferenceCycleSettings s) => new()
        {
            Cycle = s.CycleId, View = s.View, Facing = s.Facing, Height = Math.Round(s.Height, 3), X = Math.Round(s.AnchorX, 3), Y = Math.Round(s.AnchorY, 3),
            Start = s.StartFrame, Hold = s.Hold, Repeat = s.Repeat, Extend = s.Extend, Marker = s.Marker, HardEdges = s.HardEdges,
        };

        public References.ReferenceCycleSettings? ToSettings() => string.IsNullOrWhiteSpace(Cycle) ? null
            : new References.ReferenceCycleSettings(Cycle, View, Facing, Height, X, Y, Start, Hold, Repeat, Extend, Marker, HardEdges).Normalized();
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
