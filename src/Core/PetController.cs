using System.Diagnostics;

namespace TopoCursor;

/// <summary>
/// The mouse's state machine:
///   run   – the pointer is moving (animation speed follows the pointer speed)
///   idle  – stopped recently: breathes, twitches its whiskers, blinks
///   sleep – stopped for a long time: sleeps with Z's
///   bite  – on click it bites the cheese (one-shot, then back to idle/run)
///   drag  – dragging: hangs on to the cheese
///   hold  – button held but not moving during a drag: hugs the cheese
/// Platform-independent: gets input from Windows (hook) or Linux (XInput2) and draws on an IOverlay.
/// </summary>
public sealed class PetController
{
    readonly IOverlay _overlay;
    readonly IPointerSource _pointer;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    SpriteSet _sprites;
    BehaviorConfig _b;

    int _x, _y;
    bool _hasPos;
    double _lastMoveMs = -1e9;      // last "meaningful" movement
    double _lastActivityMs;         // movement or click
    double _lastMoveEventMs;
    double _speed;                  // px/s, exponential moving average
    double _jitter;                 // movement accumulated below the threshold
    bool _faceLeft;
    // direction: decided on the recent net horizontal movement, not on a single event
    const double TurnWindowMs = 150, MinTurnIntervalMs = 160;
    readonly Queue<(double t, int x)> _xHistory = new();
    double _lastTurnMs = -1e9;
    double _lastHookMs = -1e9;

    string _state = "idle";
    int _frame;
    double _frameAcc;
    bool _biting;
    double _lastTickMs;

    // dragging: button held + movement past the threshold → the mouse grabs on to the cheese
    bool _dragBtnDown;
    bool _dragging;
    int _pressX, _pressY;

    public bool Paused { get; private set; }

    public PetController(IOverlay overlay, IPointerSource pointer, SpriteSet sprites, BehaviorConfig behavior)
    {
        _overlay = overlay;
        _pointer = pointer;
        _sprites = sprites;
        _b = behavior;
        if (_pointer.TryGetPosition(out var x, out var y)) { _x = x; _y = y; _hasPos = true; }
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

    /// <summary>Movement reported by the platform's event source (hook / XInput2): the most timely one.</summary>
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

            // instantaneous speed (hook events arrive at 125–1000 Hz)
            double dt = Math.Max(now - _lastMoveEventMs, 4);
            double inst = Math.Min(dist / dt * 1000.0, 20000);
            _speed = _speed * 0.75 + inst * 0.25;
            _lastMoveEventMs = now;

            // ignore sensor micro-jitter for the state (not for the position)
            _jitter += dist;
            if (_jitter >= 2 || now - _lastMoveMs < _b.IdleAfterMs)
            {
                _jitter = 0;
                _lastMoveMs = now;
                _lastActivityMs = now;
            }

            // turning: use the net movement over the last ~150 ms, so a single "backwards" sample
            // (noise, rounding, screen-edge corrections) doesn't make the mouse turn around
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
            _biting = false; // the bite gives way to the grab
        }

        UpdateState(now);
        Render();
    }

    bool IsDragButton(PetButton button) => _b.DragButton.ToLowerInvariant() switch
    {
        "left" => button == PetButton.Left,
        "right" => button == PetButton.Right,
        "middle" => button == PetButton.Middle,
        _ => false,
    };

    public void OnButtonUp(PetButton button)
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

    public void OnButton(PetButton button)
    {
        if (IsDragButton(button))
        {
            _dragBtnDown = true;
            _pressX = _x; _pressY = _y;
        }

        bool match = _b.BiteOn.ToLowerInvariant() switch
        {
            "left" => button == PetButton.Left,
            "right" => button == PetButton.Right,
            _ => true,
        };
        if (!match) return;

        _lastActivityMs = Now;
        // repeated clicks: restart from the wind-up frame, so it looks like nibbling
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

        // Fallback only when the event source is silent (e.g. an elevated window in the foreground on Windows,
        // an X server without XInput2 on Linux). It must NOT be used while events are arriving: on Windows the hook
        // gets the new position slightly before the cursor actually moves, and polling would return the old one,
        // making the mouse "bounce" backwards (and turn around for an instant).
        if (now - _lastHookMs > 50 && _pointer.TryGetPosition(out var px, out var py) && (px != _x || py != _y)) MoveTo(px, py);

        // fallback if the button release is missed (e.g. released over an elevated window)
        if (_dragBtnDown && !_pointer.AnyButtonDown()) EndDrag();

        if (now - _lastMoveEventMs > 60) _speed *= 0.8;

        // advance the frames
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

    void UpdateState(double now)
    {
        if (_dragging)
        {
            // while moving it hangs on ("drag"); if you stop with the button held it hugs the cheese ("hold")
            bool moving = now - _lastMoveMs < _b.IdleAfterMs;
            string d = moving || !_sprites.Has("hold") ? "drag" : "hold";
            if (d != _state) SetState(d, 0);
            _lastActivityMs = now; // never falls asleep while dragging
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
