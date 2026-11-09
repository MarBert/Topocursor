using static TopoCursor.Native;

namespace TopoCursor;

/// <summary>
/// Finestra popup layered, trasparente ai click, sempre in primo piano e fuori dalla taskbar.
/// Disegna un frame con alpha per-pixel tramite UpdateLayeredWindow.
/// </summary>
public sealed class Overlay : NativeWindow, IOverlay, IDisposable
{
    public const int HotkeyId = 0x7070;
    public event Action? HotkeyPressed;

    readonly IntPtr _memDc;
    IntPtr _origBmp;
    Frame? _lastFrame;
    int _lastX = int.MinValue, _lastY = int.MinValue;
    bool _visible;

    public Overlay()
    {
        CreateHandle(new CreateParams
        {
            Caption = "TopoCursor",
            X = 0, Y = 0, Width = 1, Height = 1,
            Style = WS_POPUP,
            ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
        });
        _memDc = CreateCompatibleDC(IntPtr.Zero);
    }

    public void Draw(Frame f, int x, int y)
    {
        if (ReferenceEquals(f, _lastFrame) && x == _lastX && y == _lastY && _visible) return;

        var old = SelectObject(_memDc, (IntPtr)f.Native!);
        if (_origBmp == IntPtr.Zero) _origBmp = old;

        var dst = new POINT { X = x, Y = y };
        var size = new SIZE { cx = f.Width, cy = f.Height };
        var src = new POINT();
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0, ref blend, ULW_ALPHA);

        _lastFrame = f; _lastX = x; _lastY = y;
        if (!_visible)
        {
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            BringToTop();
            _visible = true;
        }
    }

    public void Hide()
    {
        ShowWindow(Handle, SW_HIDE);
        _visible = false;
    }

    /// <summary>Da chiamare prima di distruggere i bitmap dello sprite corrente.</summary>
    public void ReleaseFrame()
    {
        if (_origBmp != IntPtr.Zero) SelectObject(_memDc, _origBmp);
        _lastFrame = null;
    }

    /// <summary>Altre finestre topmost (taskbar, menu Start) possono finire sopra: ci riportiamo in cima.</summary>
    public void BringToTop() =>
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    IntPtr _lastCover;
    long _lastRaiseMs;

    /// <summary>
    /// Da chiamare a ogni frame. Se una finestra visibile sta sopra di noi e ci copre (menu contestuali,
    /// tooltip, taskbar, immagine di trascinamento delle icone, app "sempre in primo piano"…),
    /// torniamo in cima. Se la stessa finestra resta sopra nonostante tutto (es. menu Start, Alt+Tab,
    /// che vivono in una "banda" di z-order superiore), riproviamo solo ogni 250 ms.
    /// </summary>
    public void EnsureOnTop(long nowMs)
    {
        if (!_visible || _lastFrame == null) return;
        var cover = FindCoveringWindow(_lastX, _lastY, _lastX + _lastFrame.Width, _lastY + _lastFrame.Height);
        if (cover == IntPtr.Zero)
        {
            _lastCover = IntPtr.Zero;
            return;
        }
        if (cover == _lastCover && nowMs - _lastRaiseMs < 250) return;
        _lastCover = cover;
        _lastRaiseMs = nowMs;
        BringToTop();
    }

    IntPtr FindCoveringWindow(int left, int top, int right, int bottom)
    {
        // risale lo z-order a partire da noi: di solito non c'è niente sopra e il ciclo finisce subito
        int guard = 0;
        for (var h = GetWindow(Handle, GW_HWNDPREV); h != IntPtr.Zero && guard++ < 512; h = GetWindow(h, GW_HWNDPREV))
        {
            if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) continue;
            if (!GetWindowRect(h, out var r)) continue;
            if (r.Right <= left || r.Left >= right || r.Bottom <= top || r.Top >= bottom) continue;
            return h;
        }
        return IntPtr.Zero;
    }

    static bool IsCloaked(IntPtr h) =>
        DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    public bool RegisterToggleHotkey() =>
        RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.M);

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_NCHITTEST:
                m.Result = new IntPtr(HTTRANSPARENT);
                return;
            case WM_HOTKEY when m.WParam.ToInt32() == HotkeyId:
                HotkeyPressed?.Invoke();
                return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, HotkeyId);
        ReleaseFrame();
        DeleteDC(_memDc);
        DestroyHandle();
    }
}
