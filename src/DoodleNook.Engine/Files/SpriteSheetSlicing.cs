using System.Text.Json;
using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Files;

/// <summary>The order frames are read from a sheet: along each row (the usual way), or down each column.</summary>
public enum FrameOrder
{
    AcrossRows,
    DownColumns,
}

/// <summary>
/// How a sprite sheet is cut into frames: every frame is <see cref="FrameWidth"/> × <see cref="FrameHeight"/>; the first
/// one starts <see cref="MarginLeft"/>, <see cref="MarginTop"/> from the sheet's top-left corner; neighbouring frames are
/// <see cref="SpacingX"/> / <see cref="SpacingY"/> pixels apart. <see cref="FirstFrame"/> (1-based) and
/// <see cref="Count"/> (null: to the last frame) choose which of the sheet's frames are used.
/// </summary>
public sealed record SliceGrid(int FrameWidth, int FrameHeight, int MarginLeft = 0, int MarginTop = 0, int SpacingX = 0, int SpacingY = 0,
    FrameOrder Order = FrameOrder.AcrossRows, int FirstFrame = 1, int? Count = null);

/// <summary>Where a grid's frames are on a sheet: every whole cell in reading order, and the cells chosen.</summary>
public sealed record SliceLayout(int Columns, int Rows, IReadOnlyList<PixelRect> Cells, IReadOnlyList<PixelRect> Frames, int FirstIndex);

/// <summary>
/// Mission 015 — Sprite sheet → animation. Cuts a sheet into frames with exact pixels (no resampling, so pixel art stays
/// sharp), only ever using whole cells: a partly cut-off cell at the right or bottom edge is never a frame. The sheet
/// itself is only read.
/// </summary>
public static class SpriteSheetSlicer
{
    /// <summary>What is wrong with a grid for a sheet, in plain words; null when it can be used.</summary>
    public static string? Problem(SliceGrid g, int sheetWidth, int sheetHeight)
    {
        if (g.FrameWidth < 1 || g.FrameHeight < 1) return "The frame width and height must be at least 1 pixel.";
        if (g.MarginLeft < 0 || g.MarginTop < 0 || g.SpacingX < 0 || g.SpacingY < 0) return "Margins and spacing can't be negative.";
        if (g.MarginLeft + g.FrameWidth > sheetWidth)
            return g.MarginLeft == 0
                ? $"A frame {g.FrameWidth} px wide is wider than the sheet ({sheetWidth} px)."
                : $"A frame {g.FrameWidth} px wide doesn't fit after the {g.MarginLeft} px left margin: the sheet is {sheetWidth} px wide.";
        if (g.MarginTop + g.FrameHeight > sheetHeight)
            return g.MarginTop == 0
                ? $"A frame {g.FrameHeight} px tall is taller than the sheet ({sheetHeight} px)."
                : $"A frame {g.FrameHeight} px tall doesn't fit below the {g.MarginTop} px top margin: the sheet is {sheetHeight} px tall.";
        var (cols, rows) = CellCount(g, sheetWidth, sheetHeight);
        int cells = cols * rows;
        if (g.FirstFrame < 1 || g.FirstFrame > cells)
            return $"The sheet has {cells} frame{(cells == 1 ? "" : "s")} with these settings: the first frame must be 1 to {cells}.";
        if (g.Count is { } n && n < 1) return "Choose at least one frame.";
        return null;
    }

    /// <summary>How many whole cells fit across and down.</summary>
    public static (int Columns, int Rows) CellCount(SliceGrid g, int sheetWidth, int sheetHeight)
    {
        int Along(int size, int margin, int frame, int spacing) =>
            frame < 1 || size - margin - frame < 0 ? 0 : (size - margin - frame) / (frame + Math.Max(0, spacing)) + 1;
        return (Along(sheetWidth, g.MarginLeft, g.FrameWidth, g.SpacingX), Along(sheetHeight, g.MarginTop, g.FrameHeight, g.SpacingY));
    }

    /// <summary>The grid's cells on a sheet in reading order, and the frames chosen (empty when <see cref="Problem"/> isn't null).</summary>
    public static SliceLayout Layout(SliceGrid g, int sheetWidth, int sheetHeight)
    {
        if (Problem(g, sheetWidth, sheetHeight) is not null) return new SliceLayout(0, 0, [], [], 0);
        var (cols, rows) = CellCount(g, sheetWidth, sheetHeight);
        var cells = new List<PixelRect>(cols * rows);
        PixelRect Cell(int c, int r) => new(g.MarginLeft + c * (g.FrameWidth + g.SpacingX), g.MarginTop + r * (g.FrameHeight + g.SpacingY), g.FrameWidth, g.FrameHeight);
        if (g.Order == FrameOrder.AcrossRows)
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++) cells.Add(Cell(c, r));
        }
        else
        {
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++) cells.Add(Cell(c, r));
        }
        int first = g.FirstFrame - 1;
        int count = Math.Min(g.Count ?? cells.Count - first, cells.Count - first);
        return new SliceLayout(cols, rows, cells, cells.GetRange(first, count), first);
    }

    /// <summary>The space at the right and bottom that no cell covers (shown as a hint: often a sign of a wrong size).</summary>
    public static (int Right, int Bottom) Leftover(SliceGrid g, int sheetWidth, int sheetHeight)
    {
        var (cols, rows) = CellCount(g, sheetWidth, sheetHeight);
        if (cols == 0 || rows == 0) return (0, 0);
        int usedW = g.MarginLeft + cols * g.FrameWidth + (cols - 1) * g.SpacingX;
        int usedH = g.MarginTop + rows * g.FrameHeight + (rows - 1) * g.SpacingY;
        return (sheetWidth - usedW, sheetHeight - usedH);
    }

    /// <summary>Exact copies of the chosen frames.</summary>
    public static List<RasterImage> Slice(RasterImage sheet, SliceGrid g)
    {
        var problem = Problem(g, sheet.Width, sheet.Height);
        if (problem is not null) throw new ArgumentException(problem, nameof(g));
        return Layout(g, sheet.Width, sheet.Height).Frames.Select(sheet.Crop).ToList();
    }

    /// <summary>True when every pixel of the cell is fully transparent.</summary>
    public static bool IsEmpty(RasterImage sheet, PixelRect cell)
    {
        var r = cell.Intersect(sheet.Bounds);
        for (int y = r.Y; y < r.Bottom; y++)
        {
            var row = sheet.Row(y).Slice(r.X, r.Width);
            foreach (uint p in row)
                if (p >> 24 != 0) return false;
        }
        return true;
    }

    // ================================================================== a first guess

    /// <summary>
    /// A sensible first grid for a sheet: the exact grid when the sheet's JSON (DoodleNook's own, or a common
    /// "frames" list of x/y/w/h) is given; otherwise the smallest frame size (and gap) at which no sprite crosses a cell
    /// border; otherwise a row of square frames for a long strip. Trailing empty cells aren't counted.
    /// </summary>
    public static SliceGrid Suggest(RasterImage sheet, string? json = null)
    {
        if (json is not null && FromJson(json, sheet.Width, sheet.Height) is { } fromJson) return fromJson;
        int w = sheet.Width, h = sheet.Height;
        var (fw, gapX) = ClearSpan(sheet, vertical: true);
        var (fh, gapY) = ClearSpan(sheet, vertical: false);
        if (fw == w && fh == h)
        {
            // Nothing to go on (an opaque sheet, or one big picture): a strip of square frames if it is one.
            if (w > h && w % h == 0) fw = h;
            else if (h > w && h % w == 0) fh = w;
        }
        var grid = new SliceGrid(fw, fh, SpacingX: gapX, SpacingY: gapY);
        return grid with { Count = CountUpToLastDrawn(sheet, grid) };
    }

    /// <summary>The number of frames up to the last one with something drawn (null: all of them, or none drawn).</summary>
    private static int? CountUpToLastDrawn(RasterImage sheet, SliceGrid grid)
    {
        var cells = Layout(grid, sheet.Width, sheet.Height).Cells;
        for (int i = cells.Count - 1; i >= 0; i--)
            if (!IsEmpty(sheet, cells[i])) return i == cells.Count - 1 ? null : i + 1;
        return null;
    }

    /// <summary>
    /// The smallest frame size along one side of the sheet, and the gap between frames (none, or a transparent line or
    /// two, as many tile sheets have), at which no sprite crosses a cell border and every band between two borders has
    /// something drawn in it (empty space inside a frame isn't a border). A sprite may touch its border — characters
    /// usually stand on the bottom row of their cell — but no pixel on one side of a border may touch a pixel on the
    /// other. The whole size when nothing fits.
    /// <para>Mission 016: real sheets (OpenGameArt characters, Kenney's tile sheets) were all guessed wrongly, because
    /// both pixel lines at every border had to be empty and gaps between frames weren't considered.</para>
    /// </summary>
    private static (int Size, int Spacing) ClearSpan(RasterImage sheet, bool vertical)
    {
        int size = vertical ? sheet.Width : sheet.Height, across = vertical ? sheet.Height : sheet.Width;
        bool Opaque(int line, int i) => (vertical ? sheet.Pixels[i * sheet.Width + line] : sheet.Pixels[line * sheet.Width + i]) >> 24 != 0;
        var clear = new bool[size];
        bool any = false;
        for (int line = 0; line < size; line++)
        {
            bool c = true;
            for (int i = 0; i < across && c; i++) c = !Opaque(line, i);
            any |= clear[line] = c;
        }
        if (!any) return (size, 0);
        bool Crossed(int border)
        {
            if (clear[border - 1] || clear[border]) return false;
            for (int i = 0; i < across; i++)
                if (Opaque(border - 1, i) && Opaque(border, i)) return true;
            return false;
        }
        // Frames without gaps first; a gap only when no grid without one fits (transparent space around sprites isn't a
        // gap: 64 px of two 32 px frames is not two 31 px frames with 2 px between).
        for (int gap = 0; gap <= 2; gap++)
            for (int d = 4; d < size; d++)
            {
                int pitch = d + gap;
                if ((size + gap) % pitch != 0 || (size + gap) / pitch < 2) continue;
                int n = (size + gap) / pitch;
                bool ok = true;
                for (int k = 1; k < n && ok; k++)
                {
                    int border = k * pitch; // the first line of the next cell
                    if (gap == 0) ok = !Crossed(border);
                    else
                        for (int g = border - gap; g < border && ok; g++) ok = clear[g];
                }
                for (int k = 0; k < n && ok; k++) ok = Array.IndexOf(clear, false, k * pitch, d) >= 0;
                if (ok) return (d, gap);
            }
        return (size, 0);
    }

    /// <summary>
    /// The grid described by a sheet's JSON: DoodleNook's own export (<c>frames</c> with <c>x, y, w, h</c>) or the common
    /// form where each frame has a <c>frame</c> object with those values (as many sprite tools write). Null when the JSON
    /// isn't understood or its frames aren't one regular grid.
    /// </summary>
    public static SliceGrid? FromJson(string json, int sheetWidth, int sheetHeight)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("frames", out var frames)) return null;
            var items = frames.ValueKind switch
            {
                JsonValueKind.Array => frames.EnumerateArray().ToList(),
                JsonValueKind.Object => frames.EnumerateObject().Select(p => p.Value).ToList(),
                _ => [],
            };
            var rects = new List<PixelRect>();
            foreach (var item in items)
            {
                var src = item.TryGetProperty("frame", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : item;
                if (!(src.TryGetProperty("x", out var x) && src.TryGetProperty("y", out var y) && src.TryGetProperty("w", out var fw) && src.TryGetProperty("h", out var fh)))
                    return null;
                rects.Add(new PixelRect(x.GetInt32(), y.GetInt32(), fw.GetInt32(), fh.GetInt32()));
            }
            if (rects.Count == 0 || rects.Any(r => r.Width != rects[0].Width || r.Height != rects[0].Height)) return null;
            int w = rects[0].Width, h = rects[0].Height;
            var xs = rects.Select(r => r.X).Distinct().Order().ToList();
            var ys = rects.Select(r => r.Y).Distinct().Order().ToList();
            int sx = xs.Count > 1 ? xs[1] - xs[0] - w : 0, sy = ys.Count > 1 ? ys[1] - ys[0] - h : 0;
            var grid = new SliceGrid(w, h, xs[0], ys[0], Math.Max(0, sx), Math.Max(0, sy));
            // The listed frames must be the grid's cells, in reading order across rows (or down columns).
            foreach (var order in new[] { FrameOrder.AcrossRows, FrameOrder.DownColumns })
            {
                var g = grid with { Order = order, Count = rects.Count };
                if (Problem(g, sheetWidth, sheetHeight) is not null) return null;
                var cells = Layout(g, sheetWidth, sheetHeight).Frames;
                if (cells.Count == rects.Count && cells.SequenceEqual(rects)) return g;
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The usual frames-per-second guess: pixel-art sized frames play at 8, larger drawings at 12.</summary>
    public static double DefaultFps(SliceGrid g) => Math.Max(g.FrameWidth, g.FrameHeight) <= 256 ? 8 : 12;
}

/// <summary>Mission 015 — what a sliced sheet becomes: a plain animation, or the Mission 012 four-layer reference to redraw.</summary>
public static class SpriteSheetImport
{
    /// <summary>
    /// A regular animation, one drawing per frame on a single "Drawing" layer, transparent (no paper) like the sheet.
    /// Identical neighbouring frames become one held drawing.
    /// </summary>
    public static Document NormalDocument(IReadOnlyList<RasterImage> frames, double fps)
    {
        if (frames.Count == 0) throw new ArgumentException("Choose at least one frame.", nameof(frames));
        int w = frames[0].Width, h = frames[0].Height;
        var cels = new List<Cel?>(frames.Count);
        Cel? previous = null;
        RasterImage? previousImage = null;
        foreach (var f in frames)
        {
            if (f.Width != w || f.Height != h) throw new ArgumentException("Every frame must be the same size.", nameof(frames));
            if (previousImage is not null && previousImage.Pixels.AsSpan().SequenceEqual(f.Pixels)) cels.Add(previous);
            else
            {
                previous = f.ContentBounds().IsEmpty ? null : new Cel(f);
                cels.Add(previous);
            }
            previousImage = f;
        }
        var doc = Document.CreateAnimated(w, h, null, false, [new LayerProperties("Drawing")], [cels],
            Math.Clamp(fps, Animation.MinFps, Animation.MaxFps), loop: true, pingPong: false, Enumerable.Repeat<int?>(null, frames.Count).ToList(),
            currentFrame: 0, activeIndex: 0);
        doc.MarkUnsaved();
        return doc;
    }

    /// <summary>The Mission 012 reconstruction layers (Reference locked at 35 %, then Sketch, Outline, Coloring), from the frames.</summary>
    public static Document ReferenceDocument(IReadOnlyList<RasterImage> frames, double fps)
    {
        if (frames.Count == 0) throw new ArgumentException("Choose at least one frame.", nameof(frames));
        var source = new ImportedAnimation(frames[0].Width, frames[0].Height, frames, Enumerable.Repeat<int?>(null, frames.Count).ToList(), []);
        return AnimationImport.BuildDocument(source, null, fps);
    }
}

/// <summary>Where a smaller picture sits in its cell when the pictures for a sheet have different sizes.</summary>
public enum CellAlignment
{
    /// <summary>Feet on the same line: centred, at the bottom of the cell (characters).</summary>
    BottomCenter,
    Center,
    TopLeft,
}

/// <summary>
/// Mission 015 — Image sequence → sprite sheet. Pictures of any sizes go into cells as large as the largest picture,
/// unscaled (never stretched, never resampled) and padded with transparency; the pictures themselves are only read.
/// The sheet is then laid out by <see cref="AnimationExport.BuildSheet"/>, the same as an exported animation.
/// </summary>
public static class PicturesToSheet
{
    /// <summary>The cell size: the largest width and the largest height.</summary>
    public static (int Width, int Height) CellSize(IReadOnlyList<RasterImage> pictures) =>
        pictures.Count == 0 ? (0, 0) : (pictures.Max(p => p.Width), pictures.Max(p => p.Height));

    /// <summary>One frame per picture, each on a transparent cell-sized canvas at <paramref name="align"/>.</summary>
    public static List<ExportFrame> Frames(IReadOnlyList<RasterImage> pictures, CellAlignment align, int durationMs = 100)
    {
        if (pictures.Count == 0) throw new ArgumentException("Choose at least one picture.", nameof(pictures));
        var (w, h) = CellSize(pictures);
        RasterImage.Validate(w, h);
        var frames = new List<ExportFrame>(pictures.Count);
        for (int i = 0; i < pictures.Count; i++)
        {
            var p = pictures[i];
            if (p.Width == w && p.Height == h)
            {
                frames.Add(new ExportFrame(p, durationMs, i));
                continue;
            }
            var (x, y) = align switch
            {
                CellAlignment.TopLeft => (0, 0),
                CellAlignment.Center => ((w - p.Width) / 2, (h - p.Height) / 2),
                _ => ((w - p.Width) / 2, h - p.Height),
            };
            var cell = new RasterImage(w, h);
            cell.Paste(p, x, y);
            frames.Add(new ExportFrame(cell, durationMs, i));
        }
        return frames;
    }

    /// <summary>A sensible number of columns: all in one row up to 8 pictures, otherwise a roughly square grid.</summary>
    public static int DefaultColumns(int count) => count <= 8 ? Math.Max(1, count) : (int)Math.Ceiling(Math.Sqrt(count));
}
