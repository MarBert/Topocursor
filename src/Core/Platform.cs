namespace TopoCursor;

public enum PetButton { Left, Right, Middle }

/// <summary>Lettura diretta dello stato del puntatore (usata come fallback dal controller).</summary>
public interface IPointerSource
{
    bool TryGetPosition(out int x, out int y);
    bool AnyButtonDown();
}

/// <summary>La finestra trasparente su cui viene disegnato il topo.</summary>
public interface IOverlay
{
    void Draw(Frame frame, int x, int y);
    void Hide();
    /// <summary>Da chiamare prima di distruggere le risorse native dei frame correnti.</summary>
    void ReleaseFrame();
}

/// <summary>Crea/distrugge la risorsa nativa (HBITMAP, Pixmap X11…) associata a un frame.</summary>
public interface IFrameFactory
{
    object Create(Frame frame);
    void Destroy(object native);
}
