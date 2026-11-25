using DoodleNook.Engine.Files;
using DoodleNook.Engine.Geometry;
using DoodleNook.Engine.Imaging;
using DoodleNook.Engine.Operations;

namespace DoodleNook.Engine.Bones;

/// <summary>Assembles the character: every bone's sprite, back to front, placed by its bone. Only the artwork: bones,
/// handles and markers are drawn by the editor on top and are never part of a picture or an export.</summary>
public static class SkeletonRenderer
{
    public static RasterImage Render(SkeletonProject project, SkeletonPose pose, SkeletonAssets assets, ColorRgb? background = null)
    {
        var image = new RasterImage(project.Width, project.Height);
        if (background is { } bg) image.Fill(Pixel.Opaque(bg));
        var mode = project.PixelArt ? Interpolation.Nearest : Interpolation.Bilinear;
        foreach (var bone in project.BackToFront())
        {
            if (pose[bone.Id] is not { Attachment: { } attachment } bp || assets.Get(project, attachment) is not { } sprite) continue;
            var frame = sprite.Frames[Math.Clamp(bp.AttachmentFrame, 0, sprite.Frames.Count - 1)];
            Draw(image, frame, bp.SpriteMatrix(frame.Width, frame.Height), mode);
        }
        return image;
    }

    /// <summary>Every frame of the master timeline, ready for the animation exporters (PNG sequence, sprite sheet, GIF).</summary>
    public static List<ExportFrame> RenderAnimation(SkeletonProject project, SkeletonAssets assets, ColorRgb? background = null)
    {
        var frames = new List<ExportFrame>(project.Length);
        int ms = (int)Math.Round(1000 / project.Fps);
        for (int f = 0; f < project.Length; f++)
            frames.Add(new ExportFrame(Render(project, SkeletonEvaluator.Evaluate(project, f, assets), assets, background), ms, f));
        return frames;
    }

    /// <summary>Draws <paramref name="sprite"/> through <paramref name="spriteToImage"/> over <paramref name="target"/>.</summary>
    public static void Draw(RasterImage target, RasterImage sprite, Affine spriteToImage, Interpolation mode)
    {
        if (!spriteToImage.IsInvertible) return;
        var bounds = Resampler.TransformedBounds(spriteToImage, sprite.Width, sprite.Height);
        int x0 = Math.Max(0, bounds.X - 1), y0 = Math.Max(0, bounds.Y - 1);
        int x1 = Math.Min(target.Width, bounds.X + bounds.Width + 1), y1 = Math.Min(target.Height, bounds.Y + bounds.Height + 1);
        if (x0 >= x1 || y0 >= y1) return;
        var inv = spriteToImage.Invert();
        var dst = target.Pixels;
        int w = target.Width;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var (u, v) = inv.Apply(x + 0.5, y + 0.5);
                if (u < -1 || v < -1 || u > sprite.Width + 1 || v > sprite.Height + 1) continue;
                uint s = Resampler.Sample(sprite, u, v, mode);
                if (s == 0) continue;
                dst[y * w + x] = Pixel.Over(s, dst[y * w + x]);
            }
    }
}
