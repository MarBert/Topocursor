using System.Runtime.InteropServices;
using static TopoCursor.Linux.X11;

namespace TopoCursor.Linux;

/// <summary>
/// Finestra X11 override-redirect (niente decorazioni, il window manager non la gestisce) che non riceve input
/// (input shape vuota → i click la attraversano). Con un compositor usa un visual ARGB a 32 bit con alpha per-pixel;
/// senza compositor ritaglia la sagoma dello sprite con una bounding shape (alpha ≥ 50%).
/// Ogni frame è una Pixmap lato server: disegnare = impostarla come sfondo della finestra.
/// </summary>
internal sealed class X11Overlay : IOverlay, IFrameFactory, IDisposable
{
    sealed class NativeFrame
    {
        public nuint Pixmap;
        public nuint Mask;
    }

    readonly IntPtr _d;
    readonly nuint _root;
    readonly nuint _win;
    readonly IntPtr _visual;
    readonly int _depth;
    readonly nuint _colormap;
    readonly bool _hasShape;
    IntPtr _gc;

    Frame? _lastFrame;
    int _lastX = int.MinValue, _lastY = int.MinValue, _lastW, _lastH;
    bool _visible;

    public bool Argb { get; }
    public bool Compositor { get; private set; }
    public nuint Window => _win;

    public X11Overlay(IntPtr display)
    {
        _d = display;
        int screen = XDefaultScreen(_d);
        _root = XDefaultRootWindow(_d);
        _hasShape = XShapeQueryExtension(_d, out _, out _) != 0;
        Compositor = DetectCompositor(screen);

        if (XMatchVisualInfo(_d, screen, 32, TrueColor, out var vi) != 0)
        {
            Argb = true;
            _visual = vi.visual;
            _depth = 32;
            _colormap = XCreateColormap(_d, _root, _visual, 0);
        }
        else
        {
            _visual = XDefaultVisual(_d, screen);
            _depth = XDefaultDepth(_d, screen);
            _colormap = XDefaultColormap(_d, screen);
        }

        var attrs = new XSetWindowAttributes
        {
            override_redirect = 1,
            background_pixel = 0,
            border_pixel = 0,
            colormap = _colormap,
        };
        _win = XCreateWindow(_d, _root, 0, 0, 1, 1, 0, _depth, InputOutput, _visual,
            CWOverrideRedirect | CWBackPixel | CWBorderPixel | CWColormap, ref attrs);
        XStoreName(_d, _win, "TopoCursor");

        // tipo "drag and drop": i compositor di solito non ci mettono ombre né animazioni
        var type = XInternAtom(_d, "_NET_WM_WINDOW_TYPE", 0);
        var dnd = XInternAtom(_d, "_NET_WM_WINDOW_TYPE_DND", 0);
        XChangeProperty(_d, _win, type, XA_ATOM, 32, PropModeReplace, new[] { (nint)dnd }, 1);

        // nessuna area di input: click, rotella e hover passano alle finestre sotto
        if (_hasShape) XShapeCombineRectangles(_d, _win, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, Unsorted);
    }

    bool DetectCompositor(int screen) =>
        XGetSelectionOwner(_d, XInternAtom(_d, $"_NET_WM_CM_S{screen}", 0)) != 0;

    /// <summary>Il compositor può essere avviato/fermato mentre giriamo (es. picom): da ricontrollare ogni tanto.</summary>
    public bool RefreshCompositor()
    {
        bool now = DetectCompositor(XDefaultScreen(_d));
        if (now == Compositor) return false;
        Compositor = now;
        return true; // le maschere vanno rigenerate: il chiamante ricarica gli sprite
    }

    // ---- IFrameFactory ---------------------------------------------------------------------------

    public object Create(Frame f)
    {
        var nf = new NativeFrame { Pixmap = XCreatePixmap(_d, _win, (uint)f.Width, (uint)f.Height, (uint)_depth) };
        if (_gc == IntPtr.Zero) _gc = XCreateGC(_d, nf.Pixmap, 0, IntPtr.Zero);

        // XImage 32 bpp: i pixel premoltiplicati 0xAARRGGBB vanno bene così (con visual a 24 bit l'alpha viene ignorato)
        int bytes = f.Width * f.Height * 4;
        var data = Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.Copy((int[])(object)f.Pixels, 0, data, f.Pixels.Length);
            var img = XCreateImage(_d, _visual, (uint)_depth, ZPixmap, 0, data, (uint)f.Width, (uint)f.Height, 32, f.Width * 4);
            if (img == IntPtr.Zero) throw new InvalidOperationException("XCreateImage fallita");
            XPutImage(_d, nf.Pixmap, _gc, img, 0, 0, 0, 0, (uint)f.Width, (uint)f.Height);
            Marshal.WriteIntPtr(img, 16, IntPtr.Zero); // XImage.data: lo liberiamo noi, non XFree
            XFree(img);
        }
        finally { Marshal.FreeHGlobal(data); }

        // sagoma: serve senza compositor (niente alpha), ma la impostiamo sempre così eventuali ombre
        // o bordi aggiunti dal compositor seguono il profilo del topo invece del rettangolo
        if (_hasShape)
        {
            uint threshold = Compositor ? 1u : 128u;
            int rowBytes = (f.Width + 7) / 8;
            var bits = new byte[rowBytes * f.Height];
            for (int y = 0; y < f.Height; y++)
                for (int x = 0; x < f.Width; x++)
                    if (f.Pixels[y * f.Width + x] >> 24 >= threshold)
                        bits[y * rowBytes + (x >> 3)] |= (byte)(1 << (x & 7));
            nf.Mask = XCreateBitmapFromData(_d, _win, bits, (uint)f.Width, (uint)f.Height);
        }
        return nf;
    }

    public void Destroy(object native)
    {
        var nf = (NativeFrame)native;
        if (nf.Pixmap != 0) XFreePixmap(_d, nf.Pixmap);
        if (nf.Mask != 0) XFreePixmap(_d, nf.Mask);
        nf.Pixmap = nf.Mask = 0;
    }

    // ---- IOverlay ----------------------------------------------------------------------------------

    public void Draw(Frame f, int x, int y)
    {
        bool frameChanged = !ReferenceEquals(f, _lastFrame);
        bool moved = x != _lastX || y != _lastY;
        if (!frameChanged && !moved && _visible) return;

        var nf = (NativeFrame)f.Native!;
        if (frameChanged)
        {
            XSetWindowBackgroundPixmap(_d, _win, nf.Pixmap);
            if (nf.Mask != 0) XShapeCombineMask(_d, _win, ShapeBounding, 0, 0, nf.Mask, ShapeSet);
        }
        if (f.Width != _lastW || f.Height != _lastH)
        {
            XMoveResizeWindow(_d, _win, x, y, (uint)f.Width, (uint)f.Height);
            _lastW = f.Width; _lastH = f.Height;
        }
        else if (moved)
        {
            XMoveWindow(_d, _win, x, y);
        }
        if (frameChanged) XClearWindow(_d, _win);

        _lastFrame = f; _lastX = x; _lastY = y;
        if (!_visible)
        {
            XMapRaised(_d, _win);
            _visible = true;
        }
        XFlush(_d);
    }

    public void Hide()
    {
        XUnmapWindow(_d, _win);
        XFlush(_d);
        _visible = false;
    }

    public void ReleaseFrame()
    {
        // lo sfondo della finestra referenzia la pixmap: la sganciamo prima che venga liberata
        XSetWindowBackgroundPixmap(_d, _win, 0);
        _lastFrame = null;
        _lastW = _lastH = 0;
    }

    /// <summary>Ridisegna dopo un Expose (es. il compositor è stato riavviato).</summary>
    public void Redraw()
    {
        if (_visible) XClearWindow(_d, _win);
    }

    // ---- sempre in cima ------------------------------------------------------------------------------

    nuint _lastCover;
    long _lastRaiseMs;

    /// <summary>
    /// Se una finestra visibile ci sta sopra e ci copre (menu, tooltip, pannelli, finestre "sempre in primo piano"),
    /// torniamo in cima. Se la stessa finestra resta sopra comunque, riproviamo solo ogni 250 ms.
    /// </summary>
    public void EnsureOnTop(long nowMs)
    {
        if (!_visible || _lastFrame == null) return;
        var cover = FindCoveringWindow();
        if (cover == 0)
        {
            _lastCover = 0;
            return;
        }
        if (cover == _lastCover && nowMs - _lastRaiseMs < 250) return;
        _lastCover = cover;
        _lastRaiseMs = nowMs;
        XRaiseWindow(_d, _win);
        XFlush(_d);
    }

    nuint FindCoveringWindow()
    {
        if (XQueryTree(_d, _root, out _, out _, out var children, out uint n) == 0 || children == IntPtr.Zero) return 0;
        try
        {
            // i figli della root sono in ordine di stacking dal basso verso l'alto
            int self = -1;
            for (int i = (int)n - 1; i >= 0; i--)
                if ((nuint)Marshal.ReadIntPtr(children, i * IntPtr.Size) == _win) { self = i; break; }
            if (self < 0) return 0;

            int l = _lastX, t = _lastY, r = _lastX + _lastW, b = _lastY + _lastH;
            for (int i = self + 1; i < n; i++)
            {
                var w = (nuint)Marshal.ReadIntPtr(children, i * IntPtr.Size);
                if (XGetWindowAttributes(_d, w, out var a) == 0 || a.map_state != IsViewable) continue;
                if (a.x + a.width <= l || a.x >= r || a.y + a.height <= t || a.y >= b) continue;
                return w;
            }
            return 0;
        }
        finally { XFree(children); }
    }

    public void Dispose()
    {
        ReleaseFrame();
        if (_gc != IntPtr.Zero) XFreeGC(_d, _gc);
        XDestroyWindow(_d, _win);
        XFlush(_d); // la colormap (se nostra) viene liberata alla chiusura della connessione
    }
}
