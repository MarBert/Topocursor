using System.Runtime.InteropServices;

namespace TopoCursor.Linux;

/// <summary>Minimal P/Invoke into Xlib and the extensions in use (Shape, XFixes, XInput2). LP64 only (x86_64, arm64).</summary>
internal static class X11
{
    const string LibX11 = "libX11.so.6";
    const string LibXext = "libXext.so.6";
    const string LibXfixes = "libXfixes.so.3";
    const string LibXi = "libXi.so.6";

    // ---- constants ----------------------------------------------------------------------------
    public const int KeyPress = 2;
    public const int Expose = 12;
    public const int MappingNotify = 34;
    public const int GenericEvent = 35;

    public const uint ShiftMask = 1, LockMask = 2, ControlMask = 4, Mod1Mask = 8, Mod2Mask = 16;
    public const uint Button1Mask = 1 << 8, Button2Mask = 1 << 9, Button3Mask = 1 << 10;

    public const int TrueColor = 4;
    public const int InputOutput = 1;
    public const int ZPixmap = 2;
    public const int GrabModeAsync = 1;
    public const int IsViewable = 2;
    public const int PropModeReplace = 0;
    public const nuint XA_ATOM = 4;

    public const nuint CWBackPixel = 1 << 1, CWBorderPixel = 1 << 3, CWOverrideRedirect = 1 << 9, CWColormap = 1 << 13;

    public const int ShapeBounding = 0, ShapeInput = 2, ShapeSet = 0, Unsorted = 0;

    public const int XIAllMasterDevices = 1;
    public const int XI_RawButtonPress = 15, XI_RawButtonRelease = 16, XI_RawMotion = 17;

    // ---- structures ----------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct XSetWindowAttributes
    {
        public nuint background_pixmap;
        public nuint background_pixel;
        public nuint border_pixmap;
        public nuint border_pixel;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public nuint backing_planes;
        public nuint backing_pixel;
        public int save_under;
        public nint event_mask;
        public nint do_not_propagate_mask;
        public int override_redirect;
        public nuint colormap;
        public nuint cursor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XVisualInfo
    {
        public IntPtr visual;
        public nuint visualid;
        public int screen;
        public int depth;
        public int @class;
        public nuint red_mask, green_mask, blue_mask;
        public int colormap_size;
        public int bits_per_rgb;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XWindowAttributes
    {
        public int x, y, width, height, border_width, depth;
        public IntPtr visual;
        public nuint root;
        public int @class, bit_gravity, win_gravity, backing_store;
        public nuint backing_planes, backing_pixel;
        public int save_under;
        public nuint colormap;
        public int map_installed, map_state;
        public nint all_event_masks, your_event_mask, do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XIEventMask
    {
        public int deviceid;
        public int mask_len;
        public IntPtr mask;
    }

    // Offsets into XEvent buffers (192 bytes) — LP64 layout
    public const int Ev_KeyState = 80, Ev_KeyCode = 84;          // XKeyEvent
    public const int Cookie_Extension = 32, Cookie_EvType = 36, Cookie_Data = 48; // XGenericEventCookie
    public const int RawEvent_Detail = 56;                        // XIRawEvent.detail
    public const int XEventSize = 192;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    // ---- Xlib -----------------------------------------------------------------------------------
    [DllImport(LibX11)] public static extern int XInitThreads();
    [DllImport(LibX11)] public static extern IntPtr XOpenDisplay(string? name);
    [DllImport(LibX11)] public static extern int XCloseDisplay(IntPtr d);
    [DllImport(LibX11)] public static extern IntPtr XSetErrorHandler(XErrorHandler? handler);
    [DllImport(LibX11)] public static extern int XDefaultScreen(IntPtr d);
    [DllImport(LibX11)] public static extern nuint XDefaultRootWindow(IntPtr d);
    [DllImport(LibX11)] public static extern IntPtr XDefaultVisual(IntPtr d, int screen);
    [DllImport(LibX11)] public static extern int XDefaultDepth(IntPtr d, int screen);
    [DllImport(LibX11)] public static extern nuint XDefaultColormap(IntPtr d, int screen);
    [DllImport(LibX11)] public static extern int XConnectionNumber(IntPtr d);
    [DllImport(LibX11)] public static extern int XPending(IntPtr d);
    [DllImport(LibX11)] public static extern int XNextEvent(IntPtr d, IntPtr ev);
    [DllImport(LibX11)] public static extern int XFlush(IntPtr d);
    [DllImport(LibX11)] public static extern int XSync(IntPtr d, int discard);
    [DllImport(LibX11)] public static extern int XFree(IntPtr data);

    [DllImport(LibX11)] public static extern int XMatchVisualInfo(IntPtr d, int screen, int depth, int @class, out XVisualInfo info);
    [DllImport(LibX11)] public static extern nuint XCreateColormap(IntPtr d, nuint w, IntPtr visual, int alloc);
    [DllImport(LibX11)] public static extern nuint XCreateWindow(IntPtr d, nuint parent, int x, int y, uint w, uint h, uint border,
        int depth, uint @class, IntPtr visual, nuint valuemask, ref XSetWindowAttributes attrs);
    [DllImport(LibX11)] public static extern int XDestroyWindow(IntPtr d, nuint w);
    [DllImport(LibX11)] public static extern int XStoreName(IntPtr d, nuint w, string name);
    [DllImport(LibX11)] public static extern nuint XInternAtom(IntPtr d, string name, int onlyIfExists);
    [DllImport(LibX11)] public static extern int XChangeProperty(IntPtr d, nuint w, nuint property, nuint type, int format, int mode,
        nint[] data, int nelements);
    [DllImport(LibX11)] public static extern nuint XGetSelectionOwner(IntPtr d, nuint selection);

    [DllImport(LibX11)] public static extern int XMapRaised(IntPtr d, nuint w);
    [DllImport(LibX11)] public static extern int XUnmapWindow(IntPtr d, nuint w);
    [DllImport(LibX11)] public static extern int XRaiseWindow(IntPtr d, nuint w);
    [DllImport(LibX11)] public static extern int XMoveWindow(IntPtr d, nuint w, int x, int y);
    [DllImport(LibX11)] public static extern int XMoveResizeWindow(IntPtr d, nuint w, int x, int y, uint width, uint height);
    [DllImport(LibX11)] public static extern int XSetWindowBackgroundPixmap(IntPtr d, nuint w, nuint pixmap);
    [DllImport(LibX11)] public static extern int XClearWindow(IntPtr d, nuint w);
    [DllImport(LibX11)] public static extern int XQueryTree(IntPtr d, nuint w, out nuint root, out nuint parent, out IntPtr children, out uint n);
    [DllImport(LibX11)] public static extern int XGetWindowAttributes(IntPtr d, nuint w, out XWindowAttributes attrs);

    [DllImport(LibX11)] public static extern nuint XCreatePixmap(IntPtr d, nuint drawable, uint w, uint h, uint depth);
    [DllImport(LibX11)] public static extern int XFreePixmap(IntPtr d, nuint pixmap);
    [DllImport(LibX11)] public static extern nuint XCreateBitmapFromData(IntPtr d, nuint drawable, byte[] data, uint w, uint h);
    [DllImport(LibX11)] public static extern IntPtr XCreateGC(IntPtr d, nuint drawable, nuint valuemask, IntPtr values);
    [DllImport(LibX11)] public static extern int XFreeGC(IntPtr d, IntPtr gc);
    [DllImport(LibX11)] public static extern IntPtr XCreateImage(IntPtr d, IntPtr visual, uint depth, int format, int offset,
        IntPtr data, uint width, uint height, int bitmapPad, int bytesPerLine);
    [DllImport(LibX11)] public static extern int XPutImage(IntPtr d, nuint drawable, IntPtr gc, IntPtr image,
        int srcX, int srcY, int dstX, int dstY, uint w, uint h);

    [DllImport(LibX11)] public static extern int XQueryPointer(IntPtr d, nuint w, out nuint root, out nuint child,
        out int rootX, out int rootY, out int winX, out int winY, out uint mask);
    [DllImport(LibX11)] public static extern int XGetPointerMapping(IntPtr d, byte[] map, int n);

    [DllImport(LibX11)] public static extern nuint XStringToKeysym(string s);
    [DllImport(LibX11)] public static extern byte XKeysymToKeycode(IntPtr d, nuint keysym);
    [DllImport(LibX11)] public static extern int XGrabKey(IntPtr d, int keycode, uint modifiers, nuint grabWindow, int ownerEvents,
        int pointerMode, int keyboardMode);
    [DllImport(LibX11)] public static extern int XUngrabKey(IntPtr d, int keycode, uint modifiers, nuint grabWindow);
    [DllImport(LibX11)] public static extern int XQueryExtension(IntPtr d, string name, out int opcode, out int firstEvent, out int firstError);
    [DllImport(LibX11)] public static extern int XGetEventData(IntPtr d, IntPtr cookie);
    [DllImport(LibX11)] public static extern void XFreeEventData(IntPtr d, IntPtr cookie);

    // ---- Shape ----------------------------------------------------------------------------------
    [DllImport(LibXext)] public static extern int XShapeQueryExtension(IntPtr d, out int eventBase, out int errorBase);
    [DllImport(LibXext)] public static extern void XShapeCombineRectangles(IntPtr d, nuint w, int kind, int x, int y,
        IntPtr rects, int n, int op, int ordering);
    [DllImport(LibXext)] public static extern void XShapeCombineMask(IntPtr d, nuint w, int kind, int x, int y, nuint mask, int op);

    // ---- XFixes ---------------------------------------------------------------------------------
    [DllImport(LibXfixes)] public static extern int XFixesQueryExtension(IntPtr d, out int eventBase, out int errorBase);
    [DllImport(LibXfixes)] public static extern int XFixesQueryVersion(IntPtr d, ref int major, ref int minor);
    [DllImport(LibXfixes)] public static extern void XFixesHideCursor(IntPtr d, nuint w);
    [DllImport(LibXfixes)] public static extern void XFixesShowCursor(IntPtr d, nuint w);

    // ---- XInput2 --------------------------------------------------------------------------------
    [DllImport(LibXi)] public static extern int XIQueryVersion(IntPtr d, ref int major, ref int minor);
    [DllImport(LibXi)] public static extern int XISelectEvents(IntPtr d, nuint w, ref XIEventMask mask, int n);

    // ---- libc -----------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd { public int fd; public short events; public short revents; }
    public const short POLLIN = 1;

    [DllImport("libc", SetLastError = true)] public static extern int poll(ref PollFd fds, nuint n, int timeoutMs);
    [DllImport("libc", SetLastError = true)] public static extern int kill(int pid, int sig);
    [DllImport("libc")] public static extern uint getuid();
    public const int SIGTERM = 15;
}
