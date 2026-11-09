namespace TopoCursor;

public enum PetButton { Left, Right, Middle }

/// <summary>Direct read of the pointer state (used by the controller as a fallback).</summary>
public interface IPointerSource
{
    bool TryGetPosition(out int x, out int y);
    bool AnyButtonDown();
}

/// <summary>The transparent window the mouse is drawn on.</summary>
public interface IOverlay
{
    void Draw(Frame frame, int x, int y);
    void Hide();
    /// <summary>Call before destroying the native resources of the current frames.</summary>
    void ReleaseFrame();
}

/// <summary>Creates/destroys the native resource (HBITMAP, X11 Pixmap…) backing a frame.</summary>
public interface IFrameFactory
{
    object Create(Frame frame);
    void Destroy(object native);
}
