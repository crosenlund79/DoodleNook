// Real X11 input for the real DoodleNook process (XTest): the same events a mouse and keyboard send.
using System.Runtime.InteropServices;
static class X
{
    [DllImport("libX11.so.6")] public static extern IntPtr XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] public static extern int XFlush(IntPtr d);
    [DllImport("libX11.so.6")] public static extern int XSync(IntPtr d, bool discard);
    [DllImport("libX11.so.6")] public static extern ulong XStringToKeysym(string s);
    [DllImport("libX11.so.6")] public static extern byte XKeysymToKeycode(IntPtr d, ulong keysym);
    [DllImport("libXtst.so.6")] public static extern int XTestFakeMotionEvent(IntPtr d, int screen, int x, int y, ulong delay);
    [DllImport("libXtst.so.6")] public static extern int XTestFakeButtonEvent(IntPtr d, uint button, bool press, ulong delay);
    [DllImport("libXtst.so.6")] public static extern int XTestFakeKeyEvent(IntPtr d, uint keycode, bool press, ulong delay);
}
static class P
{
    static IntPtr d;
    static void Sync() { X.XSync(d, false); }
    static void Move(int x, int y) { X.XTestFakeMotionEvent(d, -1, x, y, 0); Sync(); }
    static void Key(string sym, bool press) { X.XTestFakeKeyEvent(d, X.XKeysymToKeycode(d, X.XStringToKeysym(sym)), press, 0); Sync(); }
    static int Main(string[] a)
    {
        d = X.XOpenDisplay(null);
        if (d == IntPtr.Zero) { Console.Error.WriteLine("no display"); return 1; }
        for (int i = 0; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "move": Move(int.Parse(a[++i]), int.Parse(a[++i])); break;
                case "click": X.XTestFakeButtonEvent(d, 1, true, 0); Sync(); Thread.Sleep(40); X.XTestFakeButtonEvent(d, 1, false, 0); Sync(); break;
                case "drag":
                {
                    int x1 = int.Parse(a[++i]), y1 = int.Parse(a[++i]), x2 = int.Parse(a[++i]), y2 = int.Parse(a[++i]), n = int.Parse(a[++i]);
                    Move(x1, y1); Thread.Sleep(60);
                    X.XTestFakeButtonEvent(d, 1, true, 0); Sync(); Thread.Sleep(30);
                    for (int s = 1; s <= n; s++) { Move(x1 + (x2 - x1) * s / n, y1 + (y2 - y1) * s / n); Thread.Sleep(12); }
                    Thread.Sleep(30); X.XTestFakeButtonEvent(d, 1, false, 0); Sync(); break;
                }
                case "key": { var k = a[++i]; Key(k, true); Thread.Sleep(30); Key(k, false); break; }
                case "chord": { var m = a[++i]; var k = a[++i]; Key(m, true); Thread.Sleep(30); Key(k, true); Thread.Sleep(30); Key(k, false); Key(m, false); break; }
                case "sleep": Thread.Sleep(int.Parse(a[++i])); break;
                default: Console.Error.WriteLine("unknown " + a[i]); return 2;
            }
        }
        return 0;
    }
}
