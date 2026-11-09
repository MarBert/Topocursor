using static TopoCursor.Native;

namespace TopoCursor;

/// <summary>
/// Hides the system cursors by replacing them with a transparent cursor (SetSystemCursor)
/// and restores them by reloading the user's cursor scheme (SPI_SETCURSORS).
/// The change only lasts for the current session: logging out or rebooting undoes it anyway.
/// </summary>
public static class CursorHider
{
    // OCR_* : normal, ibeam, wait, cross, up, sizenwse, sizenesw, sizewe, sizens, sizeall, no, hand, appstarting, help, pin, person
    static readonly uint[] All = { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651, 32671, 32672 };
    // "Pointers" only: arrow, link hand, arrow+hourglass. The I-beam and resize arrows stay visible.
    static readonly uint[] Pointers = { 32512, 32649, 32650 };

    public static bool IsHidden { get; private set; }

    public static void Hide(string mode)
    {
        var ids = mode.ToLowerInvariant() switch
        {
            "none" => Array.Empty<uint>(),
            "pointers" or "arrowonly" => Pointers,
            _ => All,
        };
        Restore();
        if (ids.Length == 0) return;

        var andMask = Enumerable.Repeat((byte)0xFF, 32 * 32 / 8).ToArray(); // fully transparent
        var xorMask = new byte[32 * 32 / 8];
        var hInst = GetModuleHandle(null);
        foreach (var id in ids)
        {
            // SetSystemCursor takes ownership of the handle: each id needs a new one.
            var blank = CreateCursor(hInst, 0, 0, 32, 32, andMask, xorMask);
            if (blank != IntPtr.Zero) SetSystemCursor(blank, id);
        }
        IsHidden = true;
    }

    public static void Restore()
    {
        SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
        IsHidden = false;
    }
}
