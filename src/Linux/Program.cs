using System.Diagnostics;
using System.Runtime.InteropServices;
using static TopoCursor.Linux.X11;

namespace TopoCursor.Linux;

/// <summary>
/// TopoCursor for Linux (X11).
///   topocursor                 start (Ctrl+C to quit, Ctrl+Alt+M to pause/resume)
///   topocursor --quit          close the running instance
///   topocursor --autostart on  start automatically at login (off to disable)
///   topocursor --reset         restore the default config and sprites in ~/.config/topocursor
///   topocursor --force         try to start even under Wayland (XWayland)
/// </summary>
internal static class Program
{
    static volatile bool _quit;
    static volatile bool _reloadRequested;
    static long _reloadAtMs = -1;              // delayed reload after a file change (debounce)
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    static int _xErrors;
    static readonly XErrorHandler ErrorHandler = OnXError; // static reference: the GC must not collect it

    static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        Interlocked.Increment(ref _xErrors);
        return 0; // don't terminate the process (Xlib's default behavior)
    }

    static string RuntimeDir =>
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } d && Directory.Exists(d)
            ? d
            : Path.GetTempPath();

    static string LockPath => Path.Combine(RuntimeDir, $"topocursor-{getuid()}.lock");
    static string PidPath => Path.Combine(RuntimeDir, $"topocursor-{getuid()}.pid");

    static int Main(string[] args)
    {
        bool Has(string a) => args.Contains(a, StringComparer.OrdinalIgnoreCase);

        if (Has("--help") || Has("-h"))
        {
            Console.WriteLine("""
                TopoCursor – your pointer becomes a little mouse (X11)

                  topocursor                 start (Ctrl+C to quit, Ctrl+Alt+M to pause/resume)
                  topocursor --quit          close the running instance
                  topocursor --autostart on  start automatically at login (off to disable)
                  topocursor --reset         restore the default config and sprites
                  topocursor --force         try anyway under Wayland

                Config and sprites: ~/.config/topocursor (reloaded automatically when you edit them)
                """);
            return 0;
        }
        if (Has("--quit")) return QuitRunning();

        int ai = Array.FindIndex(args, a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        if (ai >= 0) return Autostart(ai + 1 < args.Length ? args[ai + 1] : "on");

        ConfigStore.Init();
        if (Has("--reset"))
        {
            ConfigStore.ExtractDefaults(ConfigStore.Dir);
            Console.WriteLine($"Default config and sprites restored in {ConfigStore.Dir}");
            return 0;
        }

        if (IsWayland() && !Has("--force"))
        {
            Console.Error.WriteLine("""
                Wayland session detected: TopoCursor for Linux only works with X11.
                On Wayland an app can't read the global pointer position or draw on top of other windows.
                Pick an "Xorg"/"X11" session on the login screen, or retry with --force (under XWayland
                the mouse will follow the pointer only over X11 windows).
                """);
            return 2;
        }

        // ---- single instance ----
        FileStream lockFile;
        try
        {
            lockFile = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("TopoCursor is already running (use --quit to close it).");
            return 1;
        }

        using (lockFile)
        {
            File.WriteAllText(PidPath, Environment.ProcessId.ToString());
            try { return Run(); }
            finally { TryDelete(PidPath); }
        }
    }

    static bool IsWayland() =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    static int Run()
    {
        var d = XOpenDisplay(null);
        if (d == IntPtr.Zero)
        {
            Console.Error.WriteLine("Can't connect to the X server (is DISPLAY set?).");
            return 1;
        }
        XSetErrorHandler(ErrorHandler);

        using var signals = new SignalGuard(() => _quit = true);
        var root = XDefaultRootWindow(d);
        var overlay = new X11Overlay(d);

        var (cfg, sprites) = LoadAll(overlay, verbose: true);

        _xErrors = 0;
        var input = new X11Input(d, root, () => Interlocked.Exchange(ref _xErrors, 0) == 0);
        var pet = new PetController(overlay, input, sprites, cfg.Behavior);
        input.Moved += pet.OnMove;
        input.ButtonDown += pet.OnButton;
        input.ButtonUp += pet.OnButtonUp;

        // ---- system pointer ----
        bool xfixes = XFixesQueryExtension(d, out _, out _) != 0;
        if (xfixes) { int maj = 4, min = 0; XFixesQueryVersion(d, ref maj, ref min); xfixes = maj >= 4; }
        bool cursorHidden = false;
        void ApplyCursor(bool paused)
        {
            bool hide = !paused && xfixes && !cfg.Behavior.HideSystemCursor.Equals("none", StringComparison.OrdinalIgnoreCase);
            if (hide == cursorHidden) return;
            if (hide) XFixesHideCursor(d, root); else XFixesShowCursor(d, root);
            cursorHidden = hide;
            XFlush(d);
        }
        ApplyCursor(false);

        input.HotkeyPressed += () =>
        {
            pet.SetPaused(!pet.Paused);
            ApplyCursor(pet.Paused);
            Console.WriteLine(pet.Paused ? "Paused (normal pointer)." : "Resumed.");
        };

        using var watcher = WatchConfig();

        Console.WriteLine($"TopoCursor started – compositor: {(overlay.Compositor ? "yes" : "no")}, " +
                          $"XInput2: {(input.HasXInput2 ? "yes" : "no (polling)")}, " +
                          $"hide pointer: {(cursorHidden ? "yes" : "no")}.");
        Console.WriteLine(input.HotkeyGrabbed
            ? "Ctrl+C to quit, Ctrl+Alt+M to pause/resume."
            : "Ctrl+C to quit (Ctrl+Alt+M is already used by another application).");
        Console.WriteLine($"Config and sprites: {ConfigStore.Dir}");

        // ---- main loop ----
        var ev = Marshal.AllocHGlobal(XEventSize);
        var pfd = new PollFd { fd = XConnectionNumber(d), events = POLLIN };
        const int TickMs = 15;
        long nextTick = 0, ticks = 0;
        try
        {
            while (!_quit)
            {
                long now = Clock.ElapsedMilliseconds;
                int timeout = (int)Math.Clamp(nextTick - now, 0, TickMs);
                if (!input.HasXInput2) timeout = Math.Min(timeout, 8); // poll the buttons more often
                if (XPending(d) == 0) poll(ref pfd, 1, timeout);

                bool moved = false, expose = false;
                while (XPending(d) > 0)
                {
                    XNextEvent(d, ev);
                    moved |= input.Handle(ev, out bool ex);
                    expose |= ex;
                }
                if (moved) input.FlushMove();
                if (expose) overlay.Redraw();
                input.PollButtons();

                now = Clock.ElapsedMilliseconds;
                if (now >= nextTick)
                {
                    nextTick = now + TickMs;
                    ticks++;
                    if (!pet.Paused)
                    {
                        pet.Tick();
                        if (ticks % 3 == 0) overlay.EnsureOnTop(now);
                    }
                    // the compositor can start/stop: re-check every ~2 s and rebuild the shape masks
                    if (ticks % 133 == 0 && overlay.RefreshCompositor()) _reloadRequested = true;
                }

                long reloadAt = Interlocked.Read(ref _reloadAtMs);
                if (_reloadRequested || (reloadAt > 0 && now >= reloadAt))
                {
                    _reloadRequested = false;
                    Interlocked.Exchange(ref _reloadAtMs, -1);
                    var (ncfg, nsprites) = LoadAll(overlay, verbose: false);
                    overlay.ReleaseFrame();
                    var old = sprites;
                    cfg = ncfg; sprites = nsprites;
                    pet.Swap(sprites, cfg.Behavior);
                    old.Dispose();
                    ApplyCursor(pet.Paused); // hideSystemCursor may have changed
                    Console.WriteLine("Sprites and config reloaded.");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ev);
            if (cursorHidden) XFixesShowCursor(d, root);
            input.Dispose();
            overlay.ReleaseFrame();
            sprites.Dispose();
            overlay.Dispose();
            XCloseDisplay(d); // even if the process were killed, closing the connection restores the pointer
            Console.WriteLine("TopoCursor closed.");
        }
        return 0;
    }

    static (AppConfig, SpriteSet) LoadAll(X11Overlay factory, bool verbose)
    {
        try
        {
            var cfg = ConfigStore.Load();
            var png = File.ReadAllBytes(Path.Combine(ConfigStore.Dir, cfg.Spritesheet));
            return (cfg, SpriteSet.Load(cfg, png, factory));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Can't load the configuration: {ex.Message}");
            if (verbose) Console.Error.WriteLine("Using the default sprite.");
            var cfg = ConfigStore.LoadEmbedded();
            return (cfg, SpriteSet.Load(cfg, ConfigStore.ReadEmbedded("mouse.png"), factory));
        }
    }

    static FileSystemWatcher? WatchConfig()
    {
        try
        {
            var w = new FileSystemWatcher(ConfigStore.Dir) { IncludeSubdirectories = false };
            FileSystemEventHandler h = (_, e) =>
            {
                var ext = Path.GetExtension(e.Name ?? "").ToLowerInvariant();
                if (ext is ".json" or ".png") Interlocked.Exchange(ref _reloadAtMs, Clock.ElapsedMilliseconds + 400);
            };
            w.Changed += h; w.Created += h;
            w.Renamed += (s, e) => h(s, e);
            w.EnableRaisingEvents = true;
            return w;
        }
        catch { return null; }
    }

    // ---- --quit ------------------------------------------------------------------------------------

    static int QuitRunning()
    {
        if (!File.Exists(PidPath) || !int.TryParse(File.ReadAllText(PidPath).Trim(), out int pid))
        {
            Console.WriteLine("TopoCursor is not running.");
            return 0;
        }
        if (kill(pid, SIGTERM) != 0)
        {
            Console.WriteLine("TopoCursor is not running.");
            TryDelete(PidPath);
            return 0;
        }
        for (int i = 0; i < 30 && File.Exists(PidPath); i++) Thread.Sleep(100);
        Console.WriteLine("TopoCursor closed.");
        return 0;
    }

    // ---- --autostart ---------------------------------------------------------------------------------

    static int Autostart(string mode)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");
        var file = Path.Combine(dir, "topocursor.desktop");
        if (mode.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(file);
            Console.WriteLine("Autostart disabled.");
            return 0;
        }

        var exe = Environment.ProcessPath ?? "topocursor";
        string exec = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{exe}\" \"{typeof(Program).Assembly.Location}\""
            : $"\"{exe}\"";
        Directory.CreateDirectory(dir);
        File.WriteAllText(file, $"""
            [Desktop Entry]
            Type=Application
            Name=TopoCursor
            Comment=Your pointer becomes a little mouse
            Exec={exec}
            Terminal=false
            X-GNOME-Autostart-enabled=true

            """);
        Console.WriteLine($"Autostart enabled: {file}");
        return 0;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    /// <summary>SIGINT (Ctrl+C), SIGTERM (--quit, kill) and SIGHUP (terminal closed) → clean exit.</summary>
    sealed class SignalGuard : IDisposable
    {
        readonly PosixSignalRegistration[] _regs;

        public SignalGuard(Action onSignal)
        {
            _regs = new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP }
                .Select(s => PosixSignalRegistration.Create(s, ctx =>
                {
                    ctx.Cancel = true; // no immediate termination: the main loop exits instead
                    onSignal();
                }))
                .ToArray();
        }

        public void Dispose()
        {
            foreach (var r in _regs) r.Dispose();
        }
    }
}
