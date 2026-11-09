using System.Drawing;

namespace TopoCursor;

/// <summary>
/// An already-scaled frame. <see cref="Pixels"/> is premultiplied ARGB (0xAARRGGBB), row by row from the top:
/// in little-endian memory these are BGRA bytes, the format both UpdateLayeredWindow and 32-bit X11 expect.
/// </summary>
public sealed class Frame
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required uint[] Pixels { get; init; }
    /// <summary>Point of the (already scaled) frame that sits under the real pointer position.</summary>
    public required Point Hotspot { get; init; }
    /// <summary>Platform resource (HBITMAP on Windows, Pixmap on X11) created by the IFrameFactory.</summary>
    public object? Native { get; internal set; }
}

public sealed class Animation
{
    public required Frame[] Right { get; init; }
    public required Frame[] Left { get; init; }
    public required double Fps { get; init; }
    public int Count => Right.Length;
}

/// <summary>Row-based sprite sheet: each animation takes one row, frames go left to right.</summary>
public sealed class SpriteSet : IDisposable
{
    readonly Dictionary<string, Animation> _anims = new(StringComparer.OrdinalIgnoreCase);
    readonly IFrameFactory _factory;
    public int Scale { get; }

    SpriteSet(int scale, IFrameFactory factory)
    {
        Scale = scale;
        _factory = factory;
    }

    public Animation Get(string name) =>
        _anims.TryGetValue(name, out var a) ? a : _anims["idle"];

    public bool Has(string name) => _anims.ContainsKey(name);

    public static SpriteSet Load(AppConfig cfg, byte[] png, IFrameFactory factory)
    {
        var img = PngDecoder.Decode(png);
        int sw = img.Width, sh = img.Height;
        int fw = cfg.FrameWidth, fh = cfg.FrameHeight, s = cfg.Scale;
        bool nativeRight = !string.Equals(cfg.Facing, "left", StringComparison.OrdinalIgnoreCase);
        var set = new SpriteSet(s, factory);

        try
        {
            foreach (var (name, def) in cfg.Animations)
            {
                if (def.Frames <= 0) continue;
                if ((def.Row + 1) * fh > sh || def.Frames * fw > sw)
                    throw new InvalidDataException($"Animation \"{name}\" goes past the edges of the sprite sheet ({sw}x{sh}).");

                var asIs = new Frame[def.Frames];
                var mirrored = new Frame[def.Frames];
                for (int i = 0; i < def.Frames; i++)
                {
                    asIs[i] = MakeFrame(img.Pixels, sw, i * fw, def.Row * fh, fw, fh, s, false, cfg.Hotspot);
                    mirrored[i] = MakeFrame(img.Pixels, sw, i * fw, def.Row * fh, fw, fh, s, true, cfg.Hotspot);
                    asIs[i].Native = factory.Create(asIs[i]);
                    mirrored[i].Native = factory.Create(mirrored[i]);
                }
                set._anims[name] = new Animation
                {
                    Right = nativeRight ? asIs : mirrored,
                    Left = nativeRight ? mirrored : asIs,
                    Fps = def.Fps > 0 ? def.Fps : 8,
                };
            }
            if (!set._anims.ContainsKey("idle")) throw new InvalidDataException("Missing the \"idle\" animation.");
        }
        catch
        {
            set.Dispose();
            throw;
        }
        return set;
    }

    /// <summary>
    /// Square crop of the first "idle" frame around the hotspot, scaled to <paramref name="size"/>
    /// (nearest neighbor). Non-premultiplied ARGB pixels. Used for the tray icon.
    /// </summary>
    public static Image32 MakeIcon(AppConfig cfg, byte[] png, int size = 32)
    {
        var img = PngDecoder.Decode(png);
        var def = cfg.Animations.TryGetValue("idle", out var d) ? d : cfg.Animations.Values.First();
        int side = Math.Min(cfg.FrameWidth, cfg.FrameHeight);
        int x0 = Math.Clamp(cfg.Hotspot.X - side * 6 / 10, 0, cfg.FrameWidth - side);
        int y0 = Math.Clamp(cfg.Hotspot.Y - side / 2, 0, cfg.FrameHeight - side) + def.Row * cfg.FrameHeight;
        var px = new uint[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int sx = x0 + x * side / size, sy = y0 + y * side / size;
            if (sx < img.Width && sy < img.Height) px[y * size + x] = img.Pixels[sy * img.Width + sx];
        }
        return new Image32 { Width = size, Height = size, Pixels = px };
    }

    static Frame MakeFrame(uint[] sheet, int sheetW, int sx, int sy, int fw, int fh, int s, bool flip, Hotspot hs)
    {
        int w = fw * s, h = fh * s;
        var outPx = new uint[w * h];
        for (int y = 0; y < fh; y++)
        {
            for (int x = 0; x < fw; x++)
            {
                uint argb = sheet[(sy + y) * sheetW + sx + (flip ? fw - 1 - x : x)];
                uint a = argb >> 24;
                if (a == 0) continue;
                // premultiply alpha (required by UpdateLayeredWindow and by X11 compositors)
                uint r = ((argb >> 16) & 0xFF) * a / 255;
                uint g = ((argb >> 8) & 0xFF) * a / 255;
                uint b = (argb & 0xFF) * a / 255;
                uint p = (a << 24) | (r << 16) | (g << 8) | b;
                for (int dy = 0; dy < s; dy++)
                {
                    int row = (y * s + dy) * w + x * s;
                    for (int dx = 0; dx < s; dx++) outPx[row + dx] = p;
                }
            }
        }

        int hx = flip ? fw - 1 - hs.X : hs.X;
        return new Frame
        {
            Width = w,
            Height = h,
            Pixels = outPx,
            Hotspot = new Point(hx * s + s / 2, hs.Y * s + s / 2),
        };
    }

    public void Dispose()
    {
        foreach (var a in _anims.Values)
            foreach (var f in a.Right.Concat(a.Left))
                if (f.Native != null)
                {
                    _factory.Destroy(f.Native);
                    f.Native = null;
                }
        _anims.Clear();
    }
}
