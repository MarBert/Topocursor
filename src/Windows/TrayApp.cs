using System.Diagnostics;
using Microsoft.Win32;

namespace TopoCursor;

public sealed class TrayApp : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "TopoCursor";
    public const string QuitEventName = @"Local\TopoCursor.Quit";

    // segnalato da `TopoCursor.exe --quit` (o Ctrl+C dentro run.ps1): uscita pulita
    readonly EventWaitHandle _quitEvent = new(false, EventResetMode.AutoReset, QuitEventName);

    readonly Overlay _overlay = new();
    readonly MouseHook _hook;
    readonly PetController _pet;
    readonly NotifyIcon _tray = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    readonly FileSystemWatcher? _watcher;

    AppConfig _cfg;
    SpriteSet _sprites;
    Icon? _icon;
    IntPtr _iconHandle;
    double _reloadAt = -1;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    ToolStripMenuItem _pauseItem = null!, _startupItem = null!;
    ToolStripMenuItem _scaleMenu = null!, _hideMenu = null!;

    public TrayApp()
    {
        (_cfg, _sprites, var trayBmp) = LoadAll(showErrors: true);
        SetTrayIcon(trayBmp);

        _pet = new PetController(_overlay, WinPointer.Instance, _sprites, _cfg.Behavior);
        _hook = new MouseHook();
        _hook.Moved += _pet.OnMove;
        _hook.ButtonDown += _pet.OnButton;
        _hook.ButtonUp += _pet.OnButtonUp;

        bool hotkey = _overlay.RegisterToggleHotkey();
        _overlay.HotkeyPressed += TogglePause;

        BuildMenu(hotkey);
        _tray.Text = "TopoCursor – il tuo cursore squittisce";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => TogglePause();

        CursorHider.Hide(_cfg.Behavior.HideSystemCursor);

        _timer.Tick += OnTick;
        _timer.Start();

        // ricarica automatica quando modifichi config.json o lo spritesheet
        try
        {
            _watcher = new FileSystemWatcher(ConfigStore.Dir) { IncludeSubdirectories = false, EnableRaisingEvents = true };
            FileSystemEventHandler h = (_, e) =>
            {
                var ext = Path.GetExtension(e.Name ?? "").ToLowerInvariant();
                if (ext is ".json" or ".png") _reloadAt = _clock.Elapsed.TotalMilliseconds + 400;
            };
            _watcher.Changed += h; _watcher.Created += h;
            _watcher.Renamed += (s, e) => h(s, e);
        }
        catch { _watcher = null; }

        SystemEvents.SessionEnding += (_, _) => CursorHider.Restore();
    }

    // ---------------------------------------------------------------------------------------

    static (AppConfig, SpriteSet, Bitmap) LoadAll(bool showErrors)
    {
        try
        {
            var cfg = ConfigStore.Load();
            var png = Path.Combine(ConfigStore.Dir, cfg.Spritesheet);
            var bytes = File.ReadAllBytes(png); // niente lock sul file: si può sovrascrivere mentre gira
            var sprites = SpriteSet.Load(cfg, bytes, GdiFrameFactory.Instance);
            return (cfg, sprites, GdiFrameFactory.ToBitmap(SpriteSet.MakeIcon(cfg, bytes)));
        }
        catch (Exception ex)
        {
            if (showErrors)
                MessageBox.Show($"Non riesco a caricare la configurazione:\n{ex.Message}\n\nUso lo sprite di default.",
                    "TopoCursor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            var cfg = ConfigStore.LoadEmbedded();
            var bytes = ConfigStore.ReadEmbedded("mouse.png");
            return (cfg, SpriteSet.Load(cfg, bytes, GdiFrameFactory.Instance),
                GdiFrameFactory.ToBitmap(SpriteSet.MakeIcon(cfg, bytes)));
        }
    }

    void Reload(bool showErrors)
    {
        var (cfg, sprites, trayBmp) = LoadAll(showErrors);
        _overlay.ReleaseFrame();
        var old = _sprites;
        _cfg = cfg; _sprites = sprites;
        _pet.Swap(sprites, cfg.Behavior);
        old.Dispose();
        SetTrayIcon(trayBmp);
        if (!_pet.Paused) CursorHider.Hide(cfg.Behavior.HideSystemCursor);
        RefreshChecks();
    }

    void SetTrayIcon(Bitmap bmp)
    {
        var oldHandle = _iconHandle;
        var oldIcon = _icon;
        _iconHandle = bmp.GetHicon();
        _icon = Icon.FromHandle(_iconHandle);
        _tray.Icon = _icon;
        bmp.Dispose();
        oldIcon?.Dispose();
        if (oldHandle != IntPtr.Zero) Native.DestroyIcon(oldHandle);
    }

    void OnTick(object? sender, EventArgs e)
    {
        if (_quitEvent.WaitOne(0))
        {
            ExitThread();
            return;
        }
        if (_reloadAt > 0 && _clock.Elapsed.TotalMilliseconds >= _reloadAt)
        {
            _reloadAt = -1;
            Reload(showErrors: false);
        }
        if (_pet.Paused) return;
        _pet.Tick();
        _overlay.EnsureOnTop((long)_clock.Elapsed.TotalMilliseconds);
    }

    // ---------------------------------------------------------------------------------------

    void BuildMenu(bool hotkeyOk)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("🐭 TopoCursor") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        _pauseItem = new ToolStripMenuItem("In pausa (cursore normale)", null, (_, _) => TogglePause())
        {
            ShortcutKeyDisplayString = hotkeyOk ? "Ctrl+Alt+M" : null,
        };
        menu.Items.Add(_pauseItem);

        _scaleMenu = new ToolStripMenuItem("Dimensione");
        foreach (var s in new[] { 1, 2, 3, 4 })
            _scaleMenu.DropDownItems.Add(new ToolStripMenuItem($"{s}×", null, (_, _) => Persist(c => c.Scale = s)) { Tag = s });
        menu.Items.Add(_scaleMenu);

        _hideMenu = new ToolStripMenuItem("Nascondi cursore di sistema");
        foreach (var (label, value) in new[] { ("Sempre", "all"), ("Solo freccia e mano (testo visibile)", "pointers"), ("Mai", "none") })
            _hideMenu.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => Persist(c => c.Behavior.HideSystemCursor = value)) { Tag = value });
        menu.Items.Add(_hideMenu);

        menu.Items.Add(new ToolStripSeparator());
        _startupItem = new ToolStripMenuItem("Avvia con Windows", null, (_, _) => ToggleStartup());
        menu.Items.Add(_startupItem);
        menu.Items.Add("Apri cartella sprite/config", null, (_, _) =>
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{ConfigStore.Dir}\"") { UseShellExecute = true }));
        menu.Items.Add("Ricarica sprite", null, (_, _) => Reload(showErrors: true));
        menu.Items.Add("Ripristina sprite e config di default", null, (_, _) => ResetDefaults());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Esci", null, (_, _) => ExitThread());

        _tray.ContextMenuStrip = menu;
        RefreshChecks();
    }

    void RefreshChecks()
    {
        if (_pauseItem == null) return;
        _pauseItem.Checked = _pet.Paused;
        foreach (ToolStripMenuItem i in _scaleMenu.DropDownItems) i.Checked = (int)i.Tag! == _cfg.Scale;
        foreach (ToolStripMenuItem i in _hideMenu.DropDownItems)
            i.Checked = string.Equals((string)i.Tag!, _cfg.Behavior.HideSystemCursor, StringComparison.OrdinalIgnoreCase);
        _startupItem.Checked = IsStartupEnabled();
    }

    void Persist(Action<AppConfig> change)
    {
        try
        {
            var cfg = ConfigStore.Load();
            change(cfg);
            ConfigStore.Save(cfg);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Non riesco a salvare config.json:\n{ex.Message}", "TopoCursor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _reloadAt = -1; // il watcher scatterebbe comunque: ricarichiamo subito
        Reload(showErrors: true);
    }

    void ResetDefaults()
    {
        var ok = MessageBox.Show($"Sovrascrivo config.json e mouse.png in\n{ConfigStore.Dir}\ncon quelli di default?",
            "TopoCursor", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (ok != DialogResult.OK) return;
        try { ConfigStore.ExtractDefaults(ConfigStore.Dir); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TopoCursor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _reloadAt = -1;
        Reload(showErrors: true);
    }

    void TogglePause()
    {
        bool paused = !_pet.Paused;
        _pet.SetPaused(paused);
        if (paused) CursorHider.Restore();
        else CursorHider.Hide(_cfg.Behavior.HideSystemCursor);
        RefreshChecks();
    }

    static bool IsStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(AppName) is string;
    }

    void ToggleStartup()
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (IsStartupEnabled()) k.DeleteValue(AppName, false);
        else k.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
        RefreshChecks();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _quitEvent.Dispose();
            _watcher?.Dispose();
            _hook.Dispose();
            CursorHider.Restore();
            _tray.Visible = false;
            _tray.Dispose();
            _overlay.Dispose();
            _sprites.Dispose();
            _icon?.Dispose();
            if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
        }
        base.Dispose(disposing);
    }
}
