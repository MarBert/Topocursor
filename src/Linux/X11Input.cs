using System.Runtime.InteropServices;
using static TopoCursor.Linux.X11;

namespace TopoCursor.Linux;

/// <summary>
/// Global pointer input on X11, without "stealing" anything from other applications:
/// - XInput2 raw events on the root window: movement and clicks arrive immediately, even during other apps' grabs
///   (e.g. while you drag an icon) and even for very short touchpad taps;
/// - without XInput2, XQueryPointer polling (the controller does it by itself on every tick);
/// - global Ctrl+Alt+M shortcut via XGrabKey.
/// </summary>
internal sealed class X11Input : IPointerSource
{
    readonly IntPtr _d;
    readonly nuint _root;
    readonly int _xiOpcode = -1;
    byte[] _buttonMap = new byte[32];
    int _mapLen;
    uint _lastMask;
    readonly int _hotkeyCode;

    public bool HasXInput2 { get; }
    public bool HotkeyGrabbed { get; }

    public event Action<int, int>? Moved;
    public event Action<PetButton>? ButtonDown;
    public event Action<PetButton>? ButtonUp;
    public event Action? HotkeyPressed;

    public X11Input(IntPtr display, nuint root, Func<bool> grabSucceeded)
    {
        _d = display;
        _root = root;
        RefreshButtonMap();

        // ---- XInput2 ----
        try
        {
            if (XQueryExtension(_d, "XInputExtension", out int opcode, out _, out _) != 0)
            {
                int major = 2, minor = 2;
                if (XIQueryVersion(_d, ref major, ref minor) == 0 && (major > 2 || (major == 2 && minor >= 1)))
                {
                    // XI 2.1+: raw events always arrive, even while other clients hold grabs
                    var bits = new byte[4];
                    SetBit(bits, XI_RawButtonPress);
                    SetBit(bits, XI_RawButtonRelease);
                    SetBit(bits, XI_RawMotion);
                    var mem = Marshal.AllocHGlobal(bits.Length);
                    try
                    {
                        Marshal.Copy(bits, 0, mem, bits.Length);
                        var mask = new XIEventMask { deviceid = XIAllMasterDevices, mask_len = bits.Length, mask = mem };
                        if (XISelectEvents(_d, _root, ref mask, 1) == 0)
                        {
                            _xiOpcode = opcode;
                            HasXInput2 = true;
                        }
                    }
                    finally { Marshal.FreeHGlobal(mem); }
                }
            }
        }
        catch (DllNotFoundException) { /* libXi missing: fall back to polling */ }
        catch (EntryPointNotFoundException) { }

        // ---- Ctrl+Alt+M (with and without Caps Lock / Num Lock) ----
        _hotkeyCode = XKeysymToKeycode(_d, XStringToKeysym("m"));
        if (_hotkeyCode != 0)
        {
            foreach (var extra in new uint[] { 0, LockMask, Mod2Mask, LockMask | Mod2Mask })
                XGrabKey(_d, _hotkeyCode, ControlMask | Mod1Mask | extra, _root, 1, GrabModeAsync, GrabModeAsync);
            XSync(_d, 0);
            HotkeyGrabbed = grabSucceeded();
        }

        if (QueryPointer(out _, out _, out var m)) _lastMask = m;
    }

    static void SetBit(byte[] mask, int ev) => mask[ev >> 3] |= (byte)(1 << (ev & 7));

    void RefreshButtonMap()
    {
        // honor a left-handed mapping, if any (physical button 1 → logical 3)
        _mapLen = XGetPointerMapping(_d, _buttonMap, _buttonMap.Length);
    }

    PetButton? Logical(int physical)
    {
        int logical = physical >= 1 && physical <= _mapLen ? _buttonMap[physical - 1] : physical;
        return logical switch
        {
            1 => PetButton.Left,
            2 => PetButton.Middle,
            3 => PetButton.Right,
            _ => null, // 4–7 wheel, other extra buttons: ignored
        };
    }

    bool QueryPointer(out int x, out int y, out uint mask) =>
        XQueryPointer(_d, _root, out _, out _, out x, out y, out _, out _, out mask) != 0;

    // ---- IPointerSource ------------------------------------------------------------------------------

    public bool TryGetPosition(out int x, out int y) => QueryPointer(out x, out y, out _);

    public bool AnyButtonDown() =>
        QueryPointer(out _, out _, out var m) && (m & (Button1Mask | Button2Mask | Button3Mask)) != 0;

    // ---- events -------------------------------------------------------------------------------------

    /// <summary>Handles an X event. Returns true if it was about movement (to be coalesced).</summary>
    public bool Handle(IntPtr ev, out bool expose)
    {
        expose = false;
        int type = Marshal.ReadInt32(ev);
        switch (type)
        {
            case KeyPress:
                uint state = (uint)Marshal.ReadInt32(ev, Ev_KeyState) & (ShiftMask | ControlMask | Mod1Mask);
                if (Marshal.ReadInt32(ev, Ev_KeyCode) == _hotkeyCode && state == (ControlMask | Mod1Mask))
                    HotkeyPressed?.Invoke();
                return false;
            case MappingNotify:
                RefreshButtonMap();
                return false;
            case Expose:
                expose = true;
                return false;
            case GenericEvent when HasXInput2 && Marshal.ReadInt32(ev, Cookie_Extension) == _xiOpcode:
                if (XGetEventData(_d, ev) == 0) return false;
                try
                {
                    int evtype = Marshal.ReadInt32(ev, Cookie_EvType);
                    var data = Marshal.ReadIntPtr(ev, Cookie_Data);
                    switch (evtype)
                    {
                        case XI_RawMotion:
                            return true;
                        case XI_RawButtonPress:
                        case XI_RawButtonRelease:
                            var b = Logical(Marshal.ReadInt32(data, RawEvent_Detail));
                            if (b is { } btn)
                            {
                                // update the position before the click, so the bite/grab starts from the right spot
                                FlushMove();
                                if (evtype == XI_RawButtonPress) ButtonDown?.Invoke(btn);
                                else ButtonUp?.Invoke(btn);
                            }
                            return false;
                    }
                }
                finally { XFreeEventData(_d, ev); }
                return false;
        }
        return false;
    }

    /// <summary>After a batch of raw motion events: a single XQueryPointer for the absolute position.</summary>
    public void FlushMove()
    {
        if (QueryPointer(out int x, out int y, out var m))
        {
            _lastMask = m;
            Moved?.Invoke(x, y);
        }
    }

    /// <summary>Without XInput2: detects presses/releases by comparing the button state between polls.</summary>
    public void PollButtons()
    {
        if (HasXInput2 || !QueryPointer(out _, out _, out var m)) return;
        var pairs = new[] { (Button1Mask, PetButton.Left), (Button2Mask, PetButton.Middle), (Button3Mask, PetButton.Right) };
        foreach (var (bit, btn) in pairs)
        {
            bool was = (_lastMask & bit) != 0, now = (m & bit) != 0;
            if (now && !was) ButtonDown?.Invoke(btn);
            else if (!now && was) ButtonUp?.Invoke(btn);
        }
        _lastMask = m;
    }

    public void Dispose()
    {
        if (_hotkeyCode == 0) return;
        foreach (var extra in new uint[] { 0, LockMask, Mod2Mask, LockMask | Mod2Mask })
            XUngrabKey(_d, _hotkeyCode, ControlMask | Mod1Mask | extra, _root);
    }
}
