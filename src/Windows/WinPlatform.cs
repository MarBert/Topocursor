using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TopoCursor;

/// <summary>Core frame → HBITMAP (32-bit top-down DIB) for UpdateLayeredWindow.</summary>
public sealed class GdiFrameFactory : IFrameFactory
{
    public static readonly GdiFrameFactory Instance = new();

    public object Create(Frame f)
    {
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = f.Width,
            biHeight = -f.Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var hbmp = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (hbmp == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
        Marshal.Copy((int[])(object)f.Pixels, 0, bits, f.Pixels.Length); // already premultiplied BGRA
        return hbmp;
    }

    public void Destroy(object native) => Native.DeleteObject((IntPtr)native);

    /// <summary>Core image (non-premultiplied ARGB) → GDI+ Bitmap, for the tray icon.</summary>
    public static Bitmap ToBitmap(Image32 img)
    {
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < img.Height; y++)
                Marshal.Copy((int[])(object)img.Pixels, y * img.Width, data.Scan0 + y * data.Stride, img.Width);
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }
}

/// <summary>Pointer state read directly (the controller's fallback when the hook is silent).</summary>
public sealed class WinPointer : IPointerSource
{
    public static readonly WinPointer Instance = new();

    public bool TryGetPosition(out int x, out int y)
    {
        bool ok = Native.GetCursorPos(out var p);
        x = p.X; y = p.Y;
        return ok;
    }

    public bool AnyButtonDown() =>
        IsDown(Native.VK_LBUTTON) || IsDown(Native.VK_RBUTTON) || IsDown(Native.VK_MBUTTON);

    static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
}
