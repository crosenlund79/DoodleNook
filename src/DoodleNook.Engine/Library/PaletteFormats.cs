using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using DoodleNook.Engine.Files;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Library;

/// <summary>A palette read from a file: its colours (exactly), its name, and what the file says about it.</summary>
public sealed record PaletteFileData(string Name, IReadOnlyList<ColorRgb> Colors, string? Description = null, string? Creator = null,
    string? Source = null, string? Licence = null);

/// <summary>A file that isn't a palette at all (for example a README beside the palettes): skipped, not an error.</summary>
public sealed class NotAPaletteException(string message) : Exception(message);

/// <summary>
/// Mission 015 — the palette files a downloaded collection usually holds, each read by a small, strict reader:
/// GIMP <c>.gpl</c> (with the "#Palette Name:", "#Description:" lines palette sites add), plain hex lists <c>.hex</c>,
/// JASC and RIFF <c>.pal</c>, Paint.NET <c>.txt</c>, Adobe swatch exchange <c>.ase</c> (RGB and grey swatches) and
/// palette pictures <c>.png</c> (one colour per pixel or block, at most 256 colours). Colours are kept exactly.
/// </summary>
public static partial class PaletteFiles
{
    /// <summary>The file types <see cref="ReadFile"/> understands.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".gpl", ".hex", ".pal", ".txt", ".ase", ".png"];

    public static bool IsPaletteExtension(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Reads any supported palette file. Throws <see cref="InvalidDataException"/> for a damaged palette and
    /// <see cref="NotAPaletteException"/> for a file that isn't one.</summary>
    public static PaletteFileData ReadFile(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 4_000_000) throw new NotAPaletteException("The file is too large to be a palette.");
        var bytes = File.ReadAllBytes(path);
        string fallbackName = Path.GetFileNameWithoutExtension(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pal" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ? RiffPal(bytes, fallbackName) : JascPal(Text(bytes), fallbackName),
            ".txt" => PaintNet(Text(bytes), fallbackName),
            ".ase" => Ase(bytes, fallbackName),
            ".png" => PalettePicture(bytes, fallbackName),
            ".hex" => Hex(Text(bytes), fallbackName),
            _ => Text(bytes).TrimStart().StartsWith("GIMP Palette", StringComparison.OrdinalIgnoreCase) ? Gpl(Text(bytes), fallbackName) : Hex(Text(bytes), fallbackName),
        };
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('﻿');

    private static IEnumerable<string> Lines(string text) => text.Split('\n').Select(l => l.Trim().TrimEnd('\r'));

    private static PaletteFileData Done(string name, List<ColorRgb> colours, Dictionary<string, string> meta)
    {
        if (colours.Count == 0) throw new InvalidDataException("The file contains no colours.");
        string? Meta(params string[] keys) => keys.Select(k => meta.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return new PaletteFileData(Meta("palette name", "name") ?? name, colours, Meta("description"), Meta("author", "creator", "by"),
            Meta("source", "url", "link"), Meta("licence", "license"));
    }

    /// <summary>"#Key: value" or ";Key: value" comment lines, as palette sites write them.</summary>
    private static bool TryMeta(string line, Dictionary<string, string> meta)
    {
        var body = line.TrimStart('#', ';').Trim();
        int colon = body.IndexOf(':');
        if (colon <= 0 || colon > 20) return false;
        var key = body[..colon].Trim().ToLowerInvariant();
        if (key is not ("palette name" or "name" or "description" or "author" or "creator" or "by" or "source" or "url" or "link" or "licence" or "license" or "colors" or "colours"))
            return false;
        meta.TryAdd(key, body[(colon + 1)..].Trim());
        return true;
    }

    private static PaletteFileData Gpl(string text, string name)
    {
        var colours = new List<ColorRgb>();
        var meta = new Dictionary<string, string>();
        foreach (var line in Lines(text).Skip(1))
        {
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                TryMeta(line, meta);
                continue;
            }
            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            {
                meta["name"] = line[5..].Trim();
                continue;
            }
            if (line.StartsWith("Columns:", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b))
                colours.Add(new ColorRgb(r, g, b));
            else throw new InvalidDataException($"Not a colour line in the GIMP palette: \"{line}\".");
        }
        return Done(name, colours, meta);
    }

    private static PaletteFileData Hex(string text, string name)
    {
        var colours = new List<ColorRgb>();
        var meta = new Dictionary<string, string>();
        foreach (var line in Lines(text))
        {
            if (line.Length == 0 || TryMeta(line, meta) || line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal)) continue;
            var hex = line.TrimStart('#');
            if (hex.Length == 8) hex = hex[2..]; // AARRGGBB
            if (hex.Length == 6 && ColorRgb.TryParse("#" + hex, out var c)) colours.Add(c);
            else throw new InvalidDataException($"Not a hex colour: \"{line}\".");
        }
        return Done(name, colours, meta);
    }

    private static PaletteFileData JascPal(string text, string name)
    {
        var lines = Lines(text).Where(l => l.Length > 0).ToList();
        if (lines.Count < 3 || !lines[0].Equals("JASC-PAL", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This .pal file is neither a JASC-PAL nor a RIFF palette.");
        if (!int.TryParse(lines[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 1 || count > 4096)
            throw new InvalidDataException("The palette's colour count is missing.");
        var colours = new List<ColorRgb>(count);
        foreach (var line in lines.Skip(3).Take(count))
        {
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3 && byte.TryParse(p[0], out var r) && byte.TryParse(p[1], out var g) && byte.TryParse(p[2], out var b))
                colours.Add(new ColorRgb(r, g, b));
            else throw new InvalidDataException($"Not a colour line in the palette: \"{line}\".");
        }
        if (colours.Count != count) throw new InvalidDataException($"The palette promises {count} colours but has {colours.Count}.");
        return Done(name, colours, []);
    }

    /// <summary>A Microsoft RIFF palette: "RIFF" size "PAL " "data" size, version, count, then R G B flags per colour.</summary>
    private static PaletteFileData RiffPal(byte[] bytes, string name)
    {
        if (!bytes.AsSpan(8, 4).SequenceEqual("PAL "u8)) throw new InvalidDataException("This RIFF file isn't a palette.");
        int at = 12;
        while (at + 8 <= bytes.Length)
        {
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at + 4));
            if (size < 0 || at + 8 + size > bytes.Length) break;
            if (bytes.AsSpan(at, 4).SequenceEqual("data"u8) && size >= 4)
            {
                int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 10));
                if (4 + count * 4 > size) throw new InvalidDataException("The palette is shorter than it says.");
                var colours = new List<ColorRgb>(count);
                for (int i = 0; i < count; i++)
                {
                    int p = at + 12 + i * 4;
                    colours.Add(new ColorRgb(bytes[p], bytes[p + 1], bytes[p + 2]));
                }
                return Done(name, colours, []);
            }
            at += 8 + size + (size & 1);
        }
        throw new InvalidDataException("The RIFF palette has no colours.");
    }

    /// <summary>Paint.NET palettes: ";" comments and one colour per line as AARRGGBB (or RRGGBB).</summary>
    private static PaletteFileData PaintNet(string text, string name)
    {
        var colours = new List<ColorRgb>();
        var meta = new Dictionary<string, string>();
        bool header = text.TrimStart().StartsWith(";paint.net", StringComparison.OrdinalIgnoreCase);
        foreach (var line in Lines(text))
        {
            if (line.Length == 0) continue;
            if (line.StartsWith(';'))
            {
                TryMeta(line, meta);
                continue;
            }
            var hex = line.TrimStart('#');
            if (hex.Length is 6 or 8 && hex.All(char.IsAsciiHexDigit) && ColorRgb.TryParse("#" + (hex.Length == 8 ? hex[2..] : hex), out var c)) colours.Add(c);
            else if (header) throw new InvalidDataException($"Not a colour in the Paint.NET palette: \"{line}\".");
            else throw new NotAPaletteException("A text file that isn't a palette.");
        }
        if (colours.Count == 0 && !header) throw new NotAPaletteException("A text file that isn't a palette.");
        return Done(name, colours, meta);
    }

    /// <summary>Adobe Swatch Exchange: "ASEF", version, block count, then colour blocks (RGB and grey are read; CMYK and
    /// Lab swatches can't be converted exactly and are refused).</summary>
    private static PaletteFileData Ase(byte[] bytes, string name)
    {
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("ASEF"u8)) throw new InvalidDataException("This isn't an Adobe swatch exchange file.");
        int blocks = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8));
        var colours = new List<ColorRgb>();
        int at = 12;
        bool skipped = false;
        string? group = null;
        for (int i = 0; i < blocks && at + 6 <= bytes.Length; i++)
        {
            int type = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at));
            int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at + 2));
            int start = at + 6;
            if (length < 0 || start + length > bytes.Length) throw new InvalidDataException("The swatch file is cut short.");
            if (type is 0x0001 or 0xC001 && length >= 2)
            {
                int chars = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(start));
                int nameBytes = chars * 2;
                if (type == 0xC001)
                {
                    if (2 + nameBytes <= length) group ??= Encoding.BigEndianUnicode.GetString(bytes, start + 2, Math.Max(0, nameBytes - 2));
                }
                else if (2 + nameBytes + 4 <= length)
                {
                    int m = start + 2 + nameBytes;
                    var model = Encoding.ASCII.GetString(bytes, m, 4);
                    float F(int k) => BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(m + 4 + 4 * k));
                    byte B(float v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
                    if (model == "RGB " && 2 + nameBytes + 16 <= length) colours.Add(new ColorRgb(B(F(0)), B(F(1)), B(F(2))));
                    else if (model == "Gray" && 2 + nameBytes + 8 <= length) colours.Add(new ColorRgb(B(F(0)), B(F(0)), B(F(0))));
                    else skipped = true;
                }
            }
            at = start + length;
        }
        if (colours.Count == 0 && skipped) throw new InvalidDataException("The swatches are CMYK or Lab colours, which can't be read exactly as screen colours.");
        return Done(name, colours, group is { Length: > 0 } g ? new Dictionary<string, string> { ["name"] = g } : []);
    }

    /// <summary>A palette picture: every distinct opaque colour in reading order (a strip of pixels or of blocks).</summary>
    private static PaletteFileData PalettePicture(byte[] bytes, string name)
    {
        RasterImage image;
        try
        {
            image = PngCodec.DecodeAny(bytes); // palette pictures from other tools are often indexed-colour PNGs
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("The picture can't be read: " + ex.Message);
        }
        if ((long)image.Width * image.Height > 1024 * 1024) throw new NotAPaletteException("A picture too large to be a palette.");
        var seen = new HashSet<uint>();
        var colours = new List<ColorRgb>();
        foreach (uint p in image.Pixels)
        {
            if (p >> 24 == 0) continue;
            if (p >> 24 != 255) throw new NotAPaletteException("A picture with soft edges: not a palette.");
            if (!seen.Add(p)) continue;
            if (seen.Count > 256) throw new NotAPaletteException("A picture with more than 256 colours: not a palette.");
            colours.Add(new ColorRgb((byte)(p >> 16), (byte)(p >> 8), (byte)p));
        }
        if (colours.Count == 0) throw new NotAPaletteException("An empty picture.");
        return Done(name, colours, []);
    }
}
