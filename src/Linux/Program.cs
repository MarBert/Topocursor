using System.Diagnostics;
using System.Runtime.InteropServices;
using static TopoCursor.Linux.X11;

namespace TopoCursor.Linux;

/// <summary>
/// TopoCursor per Linux (X11).
///   topocursor                 avvia (Ctrl+C o Ctrl+Alt+M per pausa/ripresa)
///   topocursor --quit          chiude l'istanza in esecuzione
///   topocursor --autostart on  avvia automaticamente al login (off per disattivare)
///   topocursor --reset         rimette config e sprite di default in ~/.config/topocursor
///   topocursor --force         prova ad avviarsi anche sotto Wayland (XWayland)
/// </summary>
internal static class Program
{
    static volatile bool _quit;
    static volatile bool _reloadRequested;
    static long _reloadAtMs = -1;              // ricarica ritardata dopo una modifica ai file (debounce)
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    static int _xErrors;
    static readonly XErrorHandler ErrorHandler = OnXError; // riferimento statico: il GC non deve raccoglierlo

    static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        Interlocked.Increment(ref _xErrors);
        return 0; // non terminare il processo (comportamento predefinito di Xlib)
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
                TopoCursor – il cursore diventa un topolino (X11)

                  topocursor                 avvia (Ctrl+C per uscire, Ctrl+Alt+M per pausa/ripresa)
                  topocursor --quit          chiude l'istanza in esecuzione
                  topocursor --autostart on  avvio automatico al login (off per disattivarlo)
                  topocursor --reset         ripristina config e sprite di default
                  topocursor --force         prova comunque anche sotto Wayland

                Config e sprite: ~/.config/topocursor (si ricaricano da soli quando li modifichi)
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
            Console.WriteLine($"Config e sprite di default ripristinati in {ConfigStore.Dir}");
            return 0;
        }

        if (IsWayland() && !Has("--force"))
        {
            Console.Error.WriteLine("""
                Sessione Wayland rilevata: TopoCursor per Linux funziona solo con X11.
                Sotto Wayland un'app non può leggere la posizione globale del puntatore né disegnare sopra le altre finestre.
                Scegli la sessione "Xorg"/"X11" dalla schermata di login, oppure riprova con --force (sotto XWayland
                il topo seguirà il puntatore solo sopra le finestre X11).
                """);
            return 2;
        }

        // ---- istanza singola ----
        FileStream lockFile;
        try
        {
            lockFile = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("TopoCursor è già in esecuzione (usa --quit per chiuderlo).");
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
            Console.Error.WriteLine("Impossibile connettersi al server X (DISPLAY non impostato?).");
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

        // ---- cursore di sistema ----
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
            Console.WriteLine(pet.Paused ? "In pausa (cursore normale)." : "Ripreso.");
        };

        using var watcher = WatchConfig();

        Console.WriteLine($"TopoCursor avviato – compositor: {(overlay.Compositor ? "sì" : "no")}, " +
                          $"XInput2: {(input.HasXInput2 ? "sì" : "no (polling)")}, " +
                          $"nascondi cursore: {(cursorHidden ? "sì" : "no")}.");
        Console.WriteLine(input.HotkeyGrabbed
            ? "Ctrl+C per uscire, Ctrl+Alt+M per pausa/ripresa."
            : "Ctrl+C per uscire (Ctrl+Alt+M è già usata da un'altra applicazione).");
        Console.WriteLine($"Config e sprite: {ConfigStore.Dir}");

        // ---- loop principale ----
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
                if (!input.HasXInput2) timeout = Math.Min(timeout, 8); // polling dei pulsanti più fitto
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
                    // il compositor può partire/fermarsi: ogni ~2 s ricontrolliamo e rigeneriamo le sagome
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
                    ApplyCursor(pet.Paused); // hideSystemCursor potrebbe essere cambiato
                    Console.WriteLine("Sprite e config ricaricati.");
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
            XCloseDisplay(d); // anche se il processo venisse ucciso, chiudere la connessione rimette il cursore
            Console.WriteLine("TopoCursor chiuso.");
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
            Console.Error.WriteLine($"Non riesco a caricare la configurazione: {ex.Message}");
            if (verbose) Console.Error.WriteLine("Uso lo sprite di default.");
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
            Console.WriteLine("TopoCursor non è in esecuzione.");
            return 0;
        }
        if (kill(pid, SIGTERM) != 0)
        {
            Console.WriteLine("TopoCursor non è in esecuzione.");
            TryDelete(PidPath);
            return 0;
        }
        for (int i = 0; i < 30 && File.Exists(PidPath); i++) Thread.Sleep(100);
        Console.WriteLine("TopoCursor chiuso.");
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
            Console.WriteLine("Avvio automatico disattivato.");
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
            Comment=Il cursore diventa un topolino
            Exec={exec}
            Terminal=false
            X-GNOME-Autostart-enabled=true

            """);
        Console.WriteLine($"Avvio automatico attivato: {file}");
        return 0;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    /// <summary>SIGINT (Ctrl+C), SIGTERM (--quit, kill) e SIGHUP (terminale chiuso) → uscita pulita.</summary>
    sealed class SignalGuard : IDisposable
    {
        readonly PosixSignalRegistration[] _regs;

        public SignalGuard(Action onSignal)
        {
            _regs = new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP }
                .Select(s => PosixSignalRegistration.Create(s, ctx =>
                {
                    ctx.Cancel = true; // niente terminazione immediata: esce il loop principale
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
