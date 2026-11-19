using System.Runtime.InteropServices;
using DoodleNook.Engine.Imaging;
using SkiaSharp;

namespace DoodleNook.App.Canvas;

/// <summary>
/// Bridges document pixels (owned by the UI thread) to the screen (drawn on Avalonia's render thread).
///
/// The UI thread copies only the rows that changed into a private "shadow" buffer and remembers the dirty area.
/// The render thread keeps a GPU texture (an <see cref="SKSurface"/>) and uploads only that dirty area each frame,
/// so a stroke costs a few small uploads instead of re-sending the whole picture. Without a GPU (e.g. remote desktop,
/// software rendering, tests) it draws straight from the shadow buffer instead.
/// </summary>
internal sealed unsafe class DocumentTexture
{
    private readonly object _gate = new();
    private uint* _shadow;
    private int _width, _height;
    private int _generation;
    private PixelRect _dirty = PixelRect.Empty;

    // Render-thread state.
    private SKSurface? _surface;
    private GRContext? _surfaceContext;
    private int _surfaceGeneration = -1;
    private SKImage? _snapshot;

    public int Width => _width;
    public int Height => _height;

    /// <summary>True if the last frame was drawn from a GPU texture.</summary>
    public bool LastFrameUsedGpu { get; private set; }

    /// <summary>Frames drawn from the GPU texture / straight from memory (for diagnostics and the smoke test).</summary>
    public long GpuFrames { get; private set; }
    public long SoftwareFrames { get; private set; }

    /// <summary>Total pixels uploaded to the GPU since start (for diagnostics).</summary>
    public long UploadedPixels { get; private set; }

    /// <summary>Replaces everything (new or opened document). UI thread.</summary>
    public void Load(RasterImage layer)
    {
        lock (_gate)
        {
            if (layer.Width != _width || layer.Height != _height || _shadow == null)
            {
                if (_shadow != null) NativeMemory.Free(_shadow);
                _width = layer.Width;
                _height = layer.Height;
                _shadow = (uint*)NativeMemory.Alloc((nuint)((long)_width * _height * 4));
            }
            fixed (uint* src = layer.Pixels)
                Buffer.MemoryCopy(src, _shadow, (long)_width * _height * 4, (long)_width * _height * 4);
            _generation++;
            _dirty = new PixelRect(0, 0, _width, _height);
        }
    }

    /// <summary>Copies a changed area of the layer. UI thread.</summary>
    public void Update(RasterImage layer, PixelRect area)
    {
        lock (_gate)
        {
            if (_shadow == null || layer.Width != _width || layer.Height != _height) return;
            area = area.Intersect(new PixelRect(0, 0, _width, _height));
            if (area.IsEmpty) return;
            fixed (uint* src = layer.Pixels)
            {
                for (int y = area.Y; y < area.Bottom; y++)
                {
                    long offset = (long)y * _width + area.X;
                    Buffer.MemoryCopy(src + offset, _shadow + offset, area.Width * 4L, area.Width * 4L);
                }
            }
            _dirty = _dirty.Union(area);
        }
    }

    /// <summary>Draws the picture into <paramref name="dest"/>. Render thread.</summary>
    public void Draw(SKCanvas canvas, GRContext? gr, SKRect dest, SKSamplingOptions sampling)
    {
        var src = new SKRect(0, 0, _width, _height);
        if (gr is not null)
        {
            SKImage? image = PrepareGpu(gr);
            if (image is not null)
            {
                canvas.DrawImage(image, src, dest, sampling, null);
                LastFrameUsedGpu = true;
                GpuFrames++;
                return;
            }
        }

        lock (_gate)
        {
            if (_shadow == null) return;
            var info = new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var image = SKImage.FromPixels(info, (nint)_shadow, _width * 4);
            if (image is not null) canvas.DrawImage(image, src, dest, sampling, null);
            _dirty = PixelRect.Empty;
        }
        LastFrameUsedGpu = false;
        SoftwareFrames++;
    }

    private SKImage? PrepareGpu(GRContext gr)
    {
        lock (_gate)
        {
            if (_shadow == null) return null;
            if (_surface is null || _surfaceContext != gr || _surfaceGeneration != _generation)
            {
                _snapshot?.Dispose();
                _snapshot = null;
                _surface?.Dispose();
                var info = new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
                _surface = SKSurface.Create(gr, true, info, 0, GRSurfaceOrigin.TopLeft, null, true)
                           ?? SKSurface.Create(gr, true, info);
                if (_surface is null) return null; // e.g. larger than the GPU's texture limit: draw from memory instead
                _surfaceContext = gr;
                _surfaceGeneration = _generation;
                _dirty = new PixelRect(0, 0, _width, _height);
            }

            if (!_dirty.IsEmpty)
            {
                _snapshot?.Dispose(); // release first, so Skia can update the texture in place
                _snapshot = null;
                var d = _dirty;
                var subInfo = new SKImageInfo(d.Width, d.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var sub = SKImage.FromPixelCopy(subInfo, (nint)(_shadow + ((long)d.Y * _width + d.X)), _width * 4))
                using (var paint = new SKPaint { BlendMode = SKBlendMode.Src })
                {
                    if (sub is not null) _surface.Canvas.DrawImage(sub, d.X, d.Y, paint);
                }
                UploadedPixels += d.Area;
                _dirty = PixelRect.Empty;
            }
            return _snapshot ??= _surface.Snapshot();
        }
    }
}
