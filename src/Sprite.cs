using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TopoCursor;

/// <summary>Un frame già scalato e premoltiplicato, pronto per UpdateLayeredWindow.</summary>
public sealed class Frame
{
    public required IntPtr HBitmap { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>Punto del frame (già scalato) che va sotto la posizione reale del puntatore.</summary>
    public required Point Hotspot { get; init; }
}

public sealed class Animation
{
    public required Frame[] Right { get; init; }
    public required Frame[] Left { get; init; }
    public required double Fps { get; init; }
    public int Count => Right.Length;
}

/// <summary>Spritesheet a righe: ogni animazione occupa una riga, i frame vanno da sinistra a destra.</summary>
public sealed class SpriteSet : IDisposable
{
    readonly Dictionary<string, Animation> _anims = new(StringComparer.OrdinalIgnoreCase);
    public int Scale { get; }

    SpriteSet(int scale) => Scale = scale;

    public Animation Get(string name) =>
        _anims.TryGetValue(name, out var a) ? a : _anims["idle"];

    public bool Has(string name) => _anims.ContainsKey(name);

    public static SpriteSet Load(AppConfig cfg, Stream pngStream)
    {
        using var src = new Bitmap(pngStream);
        int sw = src.Width, sh = src.Height;
        var pixels = new int[sw * sh];
        var data = src.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < sh; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * sw, sw);
        }
        finally { src.UnlockBits(data); }

        int fw = cfg.FrameWidth, fh = cfg.FrameHeight, s = cfg.Scale;
        bool nativeRight = !string.Equals(cfg.Facing, "left", StringComparison.OrdinalIgnoreCase);
        var set = new SpriteSet(s);

        try
        {
            foreach (var (name, def) in cfg.Animations)
            {
                if (def.Frames <= 0) continue;
                if ((def.Row + 1) * fh > sh || def.Frames * fw > sw)
                    throw new InvalidDataException($"L'animazione \"{name}\" esce dai bordi dello spritesheet ({sw}x{sh}).");

                var asIs = new Frame[def.Frames];
                var mirrored = new Frame[def.Frames];
                for (int i = 0; i < def.Frames; i++)
                {
                    asIs[i] = MakeFrame(pixels, sw, i * fw, def.Row * fh, fw, fh, s, false, cfg.Hotspot);
                    mirrored[i] = MakeFrame(pixels, sw, i * fw, def.Row * fh, fw, fh, s, true, cfg.Hotspot);
                }
                set._anims[name] = new Animation
                {
                    Right = nativeRight ? asIs : mirrored,
                    Left = nativeRight ? mirrored : asIs,
                    Fps = def.Fps > 0 ? def.Fps : 8,
                };
            }
        }
        catch
        {
            set.Dispose();
            throw;
        }
        return set;
    }

    /// <summary>Icona per la tray: un ritaglio quadrato del primo frame di "idle" vicino all'hotspot.</summary>
    public static Bitmap MakeTrayBitmap(AppConfig cfg, Stream pngStream)
    {
        using var src = new Bitmap(pngStream);
        var def = cfg.Animations.TryGetValue("idle", out var d) ? d : cfg.Animations.Values.First();
        int side = Math.Min(cfg.FrameWidth, cfg.FrameHeight);
        int x0 = Math.Clamp(cfg.Hotspot.X - side * 6 / 10, 0, cfg.FrameWidth - side);
        int y0 = Math.Clamp(cfg.Hotspot.Y - side / 2, 0, cfg.FrameHeight - side);
        var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(src, new Rectangle(0, 0, 32, 32), new Rectangle(x0, def.Row * cfg.FrameHeight + y0, side, side), GraphicsUnit.Pixel);
        return bmp;
    }

    static Frame MakeFrame(int[] sheet, int sheetW, int sx, int sy, int fw, int fh, int s, bool flip, Hotspot hs)
    {
        int w = fw * s, h = fh * s;
        var outPx = new int[w * h];
        for (int y = 0; y < fh; y++)
        {
            for (int x = 0; x < fw; x++)
            {
                int argb = sheet[(sy + y) * sheetW + sx + (flip ? fw - 1 - x : x)];
                uint a = (uint)argb >> 24;
                if (a == 0) continue;
                // premoltiplica alpha (richiesto da UpdateLayeredWindow con AC_SRC_ALPHA)
                uint r = (((uint)argb >> 16) & 0xFF) * a / 255;
                uint g = (((uint)argb >> 8) & 0xFF) * a / 255;
                uint b = ((uint)argb & 0xFF) * a / 255;
                int p = (int)((a << 24) | (r << 16) | (g << 8) | b);
                for (int dy = 0; dy < s; dy++)
                {
                    int row = (y * s + dy) * w + x * s;
                    for (int dx = 0; dx < s; dx++) outPx[row + dx] = p;
                }
            }
        }

        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var hbmp = Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        if (hbmp == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection fallita");
        Marshal.Copy(outPx, 0, bits, outPx.Length);

        int hx = flip ? fw - 1 - hs.X : hs.X;
        return new Frame
        {
            HBitmap = hbmp,
            Width = w,
            Height = h,
            Hotspot = new Point(hx * s + s / 2, hs.Y * s + s / 2),
        };
    }

    public void Dispose()
    {
        foreach (var a in _anims.Values)
            foreach (var f in a.Right.Concat(a.Left))
                Native.DeleteObject(f.HBitmap);
        _anims.Clear();
    }
}
