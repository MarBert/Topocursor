using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TopoCursor;

public sealed class AnimDef
{
    public int Row { get; set; }
    public int Frames { get; set; } = 1;
    public double Fps { get; set; } = 8;
}

public sealed class Hotspot
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class BehaviorConfig
{
    /// <summary>Milliseconds without movement before the mouse stops running.</summary>
    public int IdleAfterMs { get; set; } = 140;
    /// <summary>Milliseconds of inactivity before the mouse falls asleep (0 = never).</summary>
    public int SleepAfterMs { get; set; } = 8000;
    public double RunFpsMin { get; set; } = 8;
    public double RunFpsMax { get; set; } = 22;
    /// <summary>Pointer speed (px/s) at which the run animation reaches RunFpsMax.</summary>
    public double RunSpeedForMaxFps { get; set; } = 1600;
    /// <summary>Horizontal pixels in the opposite direction before the mouse turns around.</summary>
    public int TurnThresholdPx { get; set; } = 6;
    /// <summary>"left", "right" or "any".</summary>
    public string BiteOn { get; set; } = "any";
    /// <summary>Button that makes the mouse "grab on" while dragging: "left", "right", "middle" or "none".</summary>
    public string DragButton { get; set; } = "left";
    /// <summary>Pixels of movement with the button held before it counts as a drag.</summary>
    public int DragThresholdPx { get; set; } = 5;
    /// <summary>"all", "pointers" (arrow, hand, busy; Windows only, same as "all" on Linux) or "none".</summary>
    public string HideSystemCursor { get; set; } = "all";
}

public sealed class AppConfig
{
    public string Spritesheet { get; set; } = "mouse.png";
    public int FrameWidth { get; set; } = 48;
    public int FrameHeight { get; set; } = 32;
    public Hotspot Hotspot { get; set; } = new() { X = 37, Y = 18 };
    /// <summary>Direction the sprite faces in the sheet: "right" or "left".</summary>
    public string Facing { get; set; } = "right";
    public int Scale { get; set; } = 2;
    public Dictionary<string, AnimDef> Animations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public BehaviorConfig Behavior { get; set; } = new();
}

/// <summary>
/// Finds/creates the config folder.
/// Portable mode: config.json next to the executable. Otherwise %APPDATA%\TopoCursor on Windows
/// or ~/.config/topocursor on Linux (created on first run with the default config and sprites).
/// </summary>
public static class ConfigStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Dir { get; private set; } = "";
    public static string ConfigPath => Path.Combine(Dir, "config.json");

    public static void Init()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "config.json")))
        {
            Dir = exeDir;
            return;
        }
        // Windows: %APPDATA%\TopoCursor — Linux: $XDG_CONFIG_HOME/topocursor (usually ~/.config/topocursor)
        Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            OperatingSystem.IsWindows() ? "TopoCursor" : "topocursor");
        Directory.CreateDirectory(Dir);
        if (!File.Exists(ConfigPath)) ExtractDefaults(Dir);
    }

    public static void ExtractDefaults(string dir)
    {
        foreach (var name in new[] { "config.json", "mouse.png" })
        {
            using var s = OpenEmbedded(name);
            using var f = File.Create(Path.Combine(dir, name));
            s.CopyTo(f);
        }
    }

    public static Stream OpenEmbedded(string name) =>
        typeof(ConfigStore).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Missing embedded resource: {name}");

    public static byte[] ReadEmbedded(string name)
    {
        using var s = OpenEmbedded(name);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static AppConfig Load()
    {
        var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Json)
                  ?? throw new InvalidDataException("config.json is empty");
        // The deserializer creates a case-sensitive dictionary: normalize it.
        cfg.Animations = new Dictionary<string, AnimDef>(cfg.Animations, StringComparer.OrdinalIgnoreCase);
        Validate(cfg);
        return cfg;
    }

    public static AppConfig LoadEmbedded()
    {
        using var s = OpenEmbedded("config.json");
        var cfg = JsonSerializer.Deserialize<AppConfig>(s, Json)!;
        cfg.Animations = new Dictionary<string, AnimDef>(cfg.Animations, StringComparer.OrdinalIgnoreCase);
        return cfg;
    }

    public static void Save(AppConfig cfg) => File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, Json));

    static void Validate(AppConfig c)
    {
        if (c.FrameWidth <= 0 || c.FrameHeight <= 0) throw new InvalidDataException("frameWidth/frameHeight must be > 0");
        foreach (var required in new[] { "run", "idle", "bite" })
            if (!c.Animations.ContainsKey(required))
                throw new InvalidDataException($"Missing required animation \"{required}\"");
        c.Scale = Math.Clamp(c.Scale, 1, 8);
    }
}
