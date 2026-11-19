using System.Runtime.InteropServices;

namespace DoodleNook.App.Platform;

/// <summary>
/// Turns off Windows' pen "helpers" for the DoodleNook window: the press-and-hold right-click ring, tap ripples,
/// barrel-button feedback and flicks. Drawing apps do this so a slow, deliberate touch-down starts a stroke instead of
/// a right-click circle, and so no visual effects appear under the nib.
/// </summary>
internal static partial class WindowsPen
{
    private const int FEEDBACK_TOUCH_CONTACTVISUALIZATION = 1;
    private const int FEEDBACK_PEN_BARRELVISUALIZATION = 2;
    private const int FEEDBACK_PEN_TAP = 3;
    private const int FEEDBACK_PEN_DOUBLETAP = 4;
    private const int FEEDBACK_PEN_PRESSANDHOLD = 5;
    private const int FEEDBACK_PEN_RIGHTTAP = 6;
    private const int FEEDBACK_TOUCH_TAP = 7;
    private const int FEEDBACK_TOUCH_DOUBLETAP = 8;
    private const int FEEDBACK_TOUCH_PRESSANDHOLD = 9;
    private const int FEEDBACK_TOUCH_RIGHTTAP = 10;
    private const int FEEDBACK_GESTURE_PRESSANDTAP = 11;
    private const int FEEDBACK_TYPE_SETTING = 4; // FEEDBACK_TYPE BOOL

    // MicrosoftTabletPenServiceProperty flags.
    private const int TABLET_DISABLE_PRESSANDHOLD = 0x00000001;
    private const int TABLET_DISABLE_PENTAPFEEDBACK = 0x00000008;
    private const int TABLET_DISABLE_PENBARRELFEEDBACK = 0x00000010;
    private const int TABLET_DISABLE_FLICKS = 0x00010000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowFeedbackSetting(nint hwnd, int feedback, uint flags, uint size, ref int configuration);

    [LibraryImport("user32.dll", EntryPoint = "SetPropW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProp(nint hwnd, string name, nint data);

    [LibraryImport("kernel32.dll", EntryPoint = "GlobalAddAtomW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial ushort GlobalAddAtom(string name);

    /// <summary>Applies the settings to a window. Returns a short status for the diagnostics panel.</summary>
    public static string Configure(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0) return "not Windows";
        int ok = 0, total = 0;
        try
        {
            foreach (var f in new[]
                     {
                         FEEDBACK_PEN_BARRELVISUALIZATION, FEEDBACK_PEN_TAP, FEEDBACK_PEN_DOUBLETAP, FEEDBACK_PEN_PRESSANDHOLD,
                         FEEDBACK_PEN_RIGHTTAP, FEEDBACK_TOUCH_CONTACTVISUALIZATION, FEEDBACK_TOUCH_TAP, FEEDBACK_TOUCH_DOUBLETAP,
                         FEEDBACK_TOUCH_PRESSANDHOLD, FEEDBACK_TOUCH_RIGHTTAP, FEEDBACK_GESTURE_PRESSANDTAP,
                     })
            {
                int off = 0;
                total++;
                if (SetWindowFeedbackSetting(hwnd, f, 0, sizeof(int), ref off)) ok++;
                _ = FEEDBACK_TYPE_SETTING;
            }
            const string prop = "MicrosoftTabletPenServiceProperty";
            GlobalAddAtom(prop);
            bool propOk = SetProp(hwnd, prop, TABLET_DISABLE_PRESSANDHOLD | TABLET_DISABLE_PENTAPFEEDBACK | TABLET_DISABLE_PENBARRELFEEDBACK | TABLET_DISABLE_FLICKS);
            return $"pen feedback off ({ok}/{total}), press-and-hold off: {(propOk ? "yes" : "no")}";
        }
        catch (Exception ex)
        {
            return "could not change pen feedback: " + ex.GetType().Name;
        }
    }
}
