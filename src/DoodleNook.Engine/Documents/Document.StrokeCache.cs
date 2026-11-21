using DoodleNook.Engine.Imaging;

namespace DoodleNook.Engine.Documents;

/// <summary>
/// Mission 011: while one layer is being painted, what lies under it doesn't change, so it is composed once per 64 px
/// tile and kept; each pen event then composes only the painted layer and the layers above it. In a picture with many
/// layers this removes most of the work per pen event (in a 12-layer picture painting the top layer, eleven of them).
/// <para>
/// The cache is used only for a visible-or-clipped top-level paint layer (not in a group), with no floating pixels or
/// previews. Any other recompositing — undo, a property change, another layer, a frame change, onion skins, a resize —
/// drops it, so it can never show anything but what a full recomposite would. Both halves use the compositor's own
/// loop split at the layer, so every pixel is identical.
/// </para>
/// </summary>
public sealed partial class Document
{
    private uint[]? _below;           // document-sized; valid only for the tiles marked in _belowTiles
    private bool[]? _belowTiles;
    private Layer? _belowLayer;
    private int _belowWidth, _belowHeight;

    /// <summary>A pen event changing fewer pixels than this (a thin line: a 128 × 128 square) is composed directly; the
    /// cache pays off for big brushes, whose events overlap the tiles already cached.</summary>
    private const int BelowCacheMinArea = 128 * 128;

    /// <summary>Forgets the cached composite under the painted layer (anything else that recomposites calls this).</summary>
    private void InvalidateBelow()
    {
        _belowLayer = null;
    }

    /// <summary>Recomposites <paramref name="area"/> after <paramref name="layer"/>'s pixels changed, using the cache when it can.
    /// Returns false when the cache can't be used (the caller recomposites normally).</summary>
    private bool RecompositeAbove(Layer layer, PixelRect area)
    {
        if (layer.ParentId != 0 || layer.Kind != LayerKind.Raster || _floating is not null || _overrides.Count > 0) return false;
        int k = _layers.IndexOf(layer);
        if (k < 2) return false; // nothing worth keeping below the first two layers
        area = area.Intersect(Bounds);
        if (area.IsEmpty) return true;

        int tilesX = TilesX, tilesY = TilesY;
        if (_below is null || _belowWidth != Width || _belowHeight != Height)
        {
            _below = new uint[Width * Height];
            _belowTiles = new bool[tilesX * tilesY];
            _belowWidth = Width;
            _belowHeight = Height;
            _belowLayer = null;
        }
        if (!ReferenceEquals(_belowLayer, layer))
        {
            Array.Clear(_belowTiles!);
            _belowLayer = layer;
        }

        // Compose the missing tiles under the layer (whole tiles, so they serve the following events too).
        int tx0 = area.X / TileSize, ty0 = area.Y / TileSize, tx1 = (area.Right - 1) / TileSize, ty1 = (area.Bottom - 1) / TileSize;
        for (int ty = ty0; ty <= ty1; ty++)
        {
            int run = -1;
            for (int tx = tx0; tx <= tx1 + 1; tx++)
            {
                bool missing = tx <= tx1 && !_belowTiles![ty * tilesX + tx];
                if (missing && run < 0) run = tx;
                if (!missing && run >= 0)
                {
                    var rect = TileRect(run, ty).Union(TileRect(tx - 1, ty));
                    Compositor.ComposeBelow(this, rect, _below!, k);
                    for (int t = run; t < tx; t++) _belowTiles![ty * tilesX + t] = true;
                    run = -1;
                }
            }
        }

        // The projection: the cached part under the layer, then the layer and everything above it.
        var dst = Projection.Pixels;
        for (int y = area.Y; y < area.Bottom; y++) Array.Copy(_below!, y * Width + area.X, dst, y * Width + area.X, area.Width);
        Layer? clipBase = null;
        for (int i = 0; i < k; i++)
            if (_layers[i].ParentId == 0 && !_layers[i].ClipToBelow) clipBase = _layers[i];
        Compositor.ComposeFrom(this, area, Projection, k, clipBase);
        ProjectionChanged?.Invoke(area);
        return true;
    }

    /// <summary>The bytes the painting cache holds now (for diagnostics).</summary>
    public long StrokeCacheBytes => _below is null ? 0 : (long)_below.Length * 4;
}
