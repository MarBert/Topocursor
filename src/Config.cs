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
    /// <summary>Dopo quanti ms senza movimento il topo smette di correre.</summary>
    public int IdleAfterMs { get; set; } = 140;
    /// <summary>Dopo quanti ms di inattività il topo si addormenta (0 = mai).</summary>
    public int SleepAfterMs { get; set; } = 8000;
    public double RunFpsMin { get; set; } = 8;
    public double RunFpsMax { get; set; } = 22;
    /// <summary>Velocità (px/s) alla quale la corsa raggiunge RunFpsMax.</summary>
    public double RunSpeedForMaxFps { get; set; } = 1600;
    /// <summary>Pixel orizzontali nella direzione opposta prima che il topo si giri.</summary>
    public int TurnThresholdPx { get; set; } = 6;
    /// <summary>"left", "right" o "any".</summary>
    public string BiteOn { get; set; } = "any";
    /// <summary>Tasto che fa "aggrappare" il topo quando trascini: "left", "right" o "none".</summary>
    public string DragButton { get; set; } = "left";
    /// <summary>Pixel di movimento col tasto premuto prima che diventi un trascinamento.</summary>
    public int DragThresholdPx { get; set; } = 5;
    /// <summary>"all", "pointers" (freccia, mano, attesa) o "none".</summary>
    public string HideSystemCursor { get; set; } = "all";
}

public sealed class AppConfig
{
    public string Spritesheet { get; set; } = "mouse.png";
    public int FrameWidth { get; set; } = 48;
    public int FrameHeight { get; set; } = 32;
    public Hotspot Hotspot { get; set; } = new() { X = 37, Y = 18 };
    /// <summary>Verso in cui guarda lo sprite nello sheet: "right" o "left".</summary>
    public string Facing { get; set; } = "right";
    public int Scale { get; set; } = 2;
    public Dictionary<string, AnimDef> Animations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public BehaviorConfig Behavior { get; set; } = new();
}

/// <summary>
/// Trova/crea la cartella di configurazione.
/// Modalità portable: config.json accanto all'exe. Altrimenti %APPDATA%\TopoCursor
/// (creata al primo avvio con config e sprite di default).
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
        Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TopoCursor");
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
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Risorsa incorporata mancante: {name}");

    public static AppConfig Load()
    {
        var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Json)
                  ?? throw new InvalidDataException("config.json vuoto");
        // Il deserializzatore crea un dizionario case-sensitive: lo normalizziamo.
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
        if (c.FrameWidth <= 0 || c.FrameHeight <= 0) throw new InvalidDataException("frameWidth/frameHeight devono essere > 0");
        foreach (var required in new[] { "run", "idle", "bite" })
            if (!c.Animations.ContainsKey(required))
                throw new InvalidDataException($"Manca l'animazione obbligatoria \"{required}\"");
        c.Scale = Math.Clamp(c.Scale, 1, 8);
    }
}
