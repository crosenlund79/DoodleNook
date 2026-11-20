using DoodleNook.Engine.Documents;
using DoodleNook.Engine.Samples;

namespace DoodleNook.App.Files;

public enum SampleAnimation
{
    BouncingBall,
    PixelSlime,
}

/// <summary>The sample animations of Help › Sample animations (drawn by code, so they are always available and fully editable).</summary>
public static class Samples
{
    public static string Name(SampleAnimation s) => s switch
    {
        SampleAnimation.BouncingBall => "Bouncing ball",
        _ => "Pixel slime",
    };

    public static bool IsPixelArt(SampleAnimation s) => s == SampleAnimation.PixelSlime;

    public static Document Open(SampleAnimation s) => s switch
    {
        SampleAnimation.BouncingBall => SampleAnimations.BouncingBall(),
        _ => SampleAnimations.PixelSlime(),
    };
}
