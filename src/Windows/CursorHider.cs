using static TopoCursor.Native;

namespace TopoCursor;

/// <summary>
/// Nasconde i cursori di sistema sostituendoli con un cursore trasparente (SetSystemCursor)
/// e li ripristina ricaricando lo schema dell'utente (SPI_SETCURSORS).
/// La modifica vale solo per la sessione corrente: un logout/riavvio la annulla comunque.
/// </summary>
public static class CursorHider
{
    // OCR_* : normal, ibeam, wait, cross, up, sizenwse, sizenesw, sizewe, sizens, sizeall, no, hand, appstarting, help, pin, person
    static readonly uint[] All = { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651, 32671, 32672 };
    // Solo i "puntatori": freccia, mano dei link, freccia+clessidra. I-beam e frecce di ridimensionamento restano visibili.
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

        var andMask = Enumerable.Repeat((byte)0xFF, 32 * 32 / 8).ToArray(); // tutto trasparente
        var xorMask = new byte[32 * 32 / 8];
        var hInst = GetModuleHandle(null);
        foreach (var id in ids)
        {
            // SetSystemCursor si prende la proprietà dell'handle: ne serve uno nuovo per ogni id.
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
