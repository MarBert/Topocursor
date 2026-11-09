using System.Diagnostics;

namespace TopoCursor;

/// <summary>
/// Macchina a stati del topolino:
///   run   – il mouse si sta muovendo (velocità dell'animazione proporzionale alla velocità del puntatore)
///   idle  – fermo da poco: respira, muove i baffi, sbatte le palpebre
///   sleep – fermo da molto: dorme con le Z
///   bite  – al click addenta il formaggio (one-shot, poi torna a idle/run)
/// </summary>
public sealed class PetController
{
    readonly Overlay _overlay;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    SpriteSet _sprites;
    BehaviorConfig _b;

    int _x, _y;
    bool _hasPos;
    double _lastMoveMs = -1e9;      // ultimo movimento "significativo"
    double _lastActivityMs;         // movimento o click
    double _lastMoveEventMs;
    double _speed;                  // px/s, media esponenziale
    double _jitter;                 // movimento accumulato sotto soglia
    bool _faceLeft;
    // direzione: decisa sullo spostamento orizzontale netto recente, non sul singolo evento
    const double TurnWindowMs = 150, MinTurnIntervalMs = 160;
    readonly Queue<(double t, int x)> _xHistory = new();
    double _lastTurnMs = -1e9;
    double _lastHookMs = -1e9;

    string _state = "idle";
    int _frame;
    double _frameAcc;
    bool _biting;
    double _lastTickMs;

    // trascinamento: tasto premuto + movimento oltre soglia → il topo si aggrappa al formaggio
    bool _dragBtnDown;
    bool _dragging;
    int _pressX, _pressY;

    public bool Paused { get; private set; }

    public PetController(Overlay overlay, SpriteSet sprites, BehaviorConfig behavior)
    {
        _overlay = overlay;
        _sprites = sprites;
        _b = behavior;
        if (Native.GetCursorPos(out var p)) { _x = p.X; _y = p.Y; _hasPos = true; }
    }

    double Now => _clock.Elapsed.TotalMilliseconds;

    public void Swap(SpriteSet sprites, BehaviorConfig behavior)
    {
        _sprites = sprites;
        _b = behavior;
        _frame = 0; _frameAcc = 0;
        Render();
    }

    public void SetPaused(bool paused)
    {
        Paused = paused;
        if (paused) _overlay.Hide();
        else { _lastActivityMs = Now; Render(); }
    }

    /// <summary>Movimento riportato dall'hook (fonte principale e più tempestiva).</summary>
    public void OnMove(int x, int y)
    {
        _lastHookMs = Now;
        MoveTo(x, y);
    }

    void MoveTo(int x, int y)
    {
        double now = Now;
        if (_hasPos)
        {
            int dx = x - _x, dy = y - _y;
            if (dx == 0 && dy == 0) return;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            // velocità istantanea (gli eventi dell'hook arrivano a 125–1000 Hz)
            double dt = Math.Max(now - _lastMoveEventMs, 4);
            double inst = Math.Min(dist / dt * 1000.0, 20000);
            _speed = _speed * 0.75 + inst * 0.25;
            _lastMoveEventMs = now;

            // ignora micro-vibrazioni del sensore per lo stato (non per la posizione)
            _jitter += dist;
            if (_jitter >= 2 || now - _lastMoveMs < _b.IdleAfterMs)
            {
                _jitter = 0;
                _lastMoveMs = now;
                _lastActivityMs = now;
            }

            // giro: conta lo spostamento netto degli ultimi ~150 ms, così un singolo campione
            // "all'indietro" (rumore, arrotondamenti, correzioni ai bordi) non fa girare il topo
            _xHistory.Enqueue((now, x));
            while (_xHistory.Count > 1 && now - _xHistory.Peek().t > TurnWindowMs) _xHistory.Dequeue();
            int net = x - _xHistory.Peek().x;
            if (net != 0 && (net < 0) != _faceLeft &&
                Math.Abs(net) >= _b.TurnThresholdPx && now - _lastTurnMs >= MinTurnIntervalMs)
            {
                _faceLeft = net < 0;
                _lastTurnMs = now;
            }
        }
        _x = x; _y = y; _hasPos = true;

        if (_dragBtnDown && !_dragging && _sprites.Has("drag") &&
            Math.Abs(x - _pressX) + Math.Abs(y - _pressY) >= _b.DragThresholdPx)
        {
            _dragging = true;
            _biting = false; // il morso lascia il posto alla presa
        }

        UpdateState(now);
        Render();
    }

    bool IsDragButton(MouseButtons button) => _b.DragButton.ToLowerInvariant() switch
    {
        "left" => button == MouseButtons.Left,
        "right" => button == MouseButtons.Right,
        "middle" => button == MouseButtons.Middle,
        _ => false,
    };

    public void OnButtonUp(MouseButtons button)
    {
        if (IsDragButton(button)) EndDrag();
    }

    void EndDrag()
    {
        _dragBtnDown = false;
        if (!_dragging) return;
        _dragging = false;
        _lastActivityMs = Now;
        UpdateState(Now);
        Render();
    }

    public void OnButton(MouseButtons button)
    {
        if (IsDragButton(button))
        {
            _dragBtnDown = true;
            _pressX = _x; _pressY = _y;
        }

        bool match = _b.BiteOn.ToLowerInvariant() switch
        {
            "left" => button == MouseButtons.Left,
            "right" => button == MouseButtons.Right,
            _ => true,
        };
        if (!match) return;

        _lastActivityMs = Now;
        // click ripetuti: riparte dallo slancio, così sembra che sgranocchi
        int restart = _biting ? Math.Min(1, _sprites.Get("bite").Count - 1) : 0;
        _biting = true;
        SetState("bite", restart);
        Render();
    }

    public void Tick()
    {
        double now = Now;
        double dt = Math.Clamp(now - _lastTickMs, 0, 250);
        _lastTickMs = now;

        // Fallback solo se l'hook tace (es. finestra con privilegi più alti in primo piano).
        // Mentre l'hook è attivo NON va usato: l'hook riceve la posizione nuova un attimo prima che
        // Windows sposti davvero il cursore, e GetCursorPos potrebbe restituire quella vecchia
        // facendo "rimbalzare" il topo all'indietro (e girarsi per un istante).
        if (now - _lastHookMs > 50 && Native.GetCursorPos(out var p) && (p.X != _x || p.Y != _y)) MoveTo(p.X, p.Y);

        // fallback se l'hook perde il rilascio del tasto (es. rilasciato sopra una finestra elevata)
        if (_dragBtnDown && !AnyButtonDown()) EndDrag();

        if (now - _lastMoveEventMs > 60) _speed *= 0.8;

        // avanza i frame
        var anim = _sprites.Get(_state);
        double fps = _state is "run" or "drag" ? RunFps() * anim.Fps / Math.Max(_sprites.Get("run").Fps, 1) : anim.Fps;
        _frameAcc += dt / 1000.0 * fps;
        while (_frameAcc >= 1)
        {
            _frameAcc -= 1;
            _frame++;
            if (_frame >= anim.Count)
            {
                if (_biting)
                {
                    _biting = false;
                    _frame = anim.Count - 1;
                    break;
                }
                _frame = 0;
            }
        }

        UpdateState(now);
        Render();
    }

    double RunFps()
    {
        double t = Math.Clamp(_speed / Math.Max(_b.RunSpeedForMaxFps, 1), 0, 1);
        return _b.RunFpsMin + (_b.RunFpsMax - _b.RunFpsMin) * t;
    }

    static bool AnyButtonDown() =>
        ((Native.GetAsyncKeyState(Native.VK_LBUTTON) | Native.GetAsyncKeyState(Native.VK_RBUTTON) |
          Native.GetAsyncKeyState(Native.VK_MBUTTON)) & 0x8000) != 0;

    void UpdateState(double now)
    {
        if (_dragging)
        {
            // mentre si muove resta appeso ("drag"), se ti fermi col tasto premuto abbraccia il formaggio ("hold")
            bool moving = now - _lastMoveMs < _b.IdleAfterMs;
            string d = moving || !_sprites.Has("hold") ? "drag" : "hold";
            if (d != _state) SetState(d, 0);
            _lastActivityMs = now; // non si addormenta mentre trascini
            return;
        }
        if (_biting) return;
        string next;
        if (now - _lastMoveMs < _b.IdleAfterMs) next = "run";
        else if (_b.SleepAfterMs > 0 && now - _lastActivityMs > _b.SleepAfterMs && _sprites.Has("sleep")) next = "sleep";
        else next = "idle";
        if (next != _state) SetState(next, 0);
    }

    void SetState(string state, int frame)
    {
        _state = state;
        _frame = frame;
        _frameAcc = 0;
    }

    void Render()
    {
        if (Paused || !_hasPos) return;
        var anim = _sprites.Get(_state);
        var frames = _faceLeft ? anim.Left : anim.Right;
        var f = frames[Math.Clamp(_frame, 0, frames.Length - 1)];
        _overlay.Draw(f, _x - f.Hotspot.X, _y - f.Hotspot.Y);
    }
}
