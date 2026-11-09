using System.Runtime.InteropServices;
using static TopoCursor.Native;

namespace TopoCursor;

/// <summary>Hook globale low-level sul mouse: posizione immediata e click, senza consumarli.</summary>
public sealed class MouseHook : IDisposable
{
    readonly LowLevelMouseProc _proc; // va tenuto in vita finché l'hook è attivo
    IntPtr _hook;

    public event Action<int, int>? Moved;
    public event Action<MouseButtons>? ButtonDown;
    public event Action<MouseButtons>? ButtonUp;

    public MouseHook()
    {
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"SetWindowsHookEx fallito (errore {Marshal.GetLastWin32Error()})");
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                switch ((int)wParam)
                {
                    case WM_MOUSEMOVE: Moved?.Invoke(info.pt.X, info.pt.Y); break;
                    case WM_LBUTTONDOWN: ButtonDown?.Invoke(MouseButtons.Left); break;
                    case WM_RBUTTONDOWN: ButtonDown?.Invoke(MouseButtons.Right); break;
                    case WM_MBUTTONDOWN: ButtonDown?.Invoke(MouseButtons.Middle); break;
                    case WM_LBUTTONUP: ButtonUp?.Invoke(MouseButtons.Left); break;
                    case WM_RBUTTONUP: ButtonUp?.Invoke(MouseButtons.Right); break;
                    case WM_MBUTTONUP: ButtonUp?.Invoke(MouseButtons.Middle); break;
                }
            }
            catch
            {
                // Mai far fallire l'hook: Windows lo rimuoverebbe e il mouse sembrerebbe "laggare".
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
