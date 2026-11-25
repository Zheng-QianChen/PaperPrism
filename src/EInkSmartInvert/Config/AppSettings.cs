using System.Text.Json;
using System.Text.Json.Serialization;

namespace EInkSmartInvert.Config;

public enum FilterMode : uint
{
    Off = 0,
    SmartAuto = 1,
    ForceInvertAll = 2,
    CrispLightOnly = 3
}

public enum AppRuleMode : uint
{
    FollowGlobal = 0,
    SmartAuto = 1,
    ForceInvertAll = 2,
    CrispLightOnly = 3,
    PassthroughOff = 4
}

public sealed class AppInvertRule
{
    private string _matchProcesses = string.Empty;
    private string[] _parsedTokens = Array.Empty<string>();

    public string Name { get; set; } = string.Empty;

    public string MatchProcesses
    {
        get => _matchProcesses;
        set
        {
            _matchProcesses = value ?? string.Empty;
            _parsedTokens = ParseTokens(_matchProcesses);
        }
    }

    public bool Enabled { get; set; } = true;
    public AppRuleMode RuleMode { get; set; } = AppRuleMode.FollowGlobal;

    private static string[] ParseTokens(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var parts = raw.Split(new[] { ';', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                parts[i] = parts[i][..^4];
        }
        return parts;
    }

    public bool MatchesProcess(ReadOnlySpan<char> processName)
    {
        if (processName.IsEmpty)
            return false;

        var tokens = _parsedTokens;
        for (int i = 0; i < tokens.Length; i++)
        {
            if (processName.Equals(tokens[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public AppInvertRule Clone() => new()
    {
        Name = Name,
        MatchProcesses = MatchProcesses,
        Enabled = Enabled,
        RuleMode = RuleMode
    };
}

public sealed class AppSettings
{
    public string TargetMonitorDevice { get; set; } = "AUTO_SECONDARY";
    public FilterMode Mode { get; set; } = FilterMode.SmartAuto;
    public bool VirtualDisplayMode { get; set; } = true;

    // Per-Application Smart Invert / Whitelist Filter
    public bool OnlyInvertListedApps { get; set; } = false;
    public List<AppInvertRule> AppRules { get; set; } = CreateDefaultAppRules();

    // 4 Core Smart Invert Features
    public bool KeepLightWindows { get; set; } = true;
    public bool ProtectPhotos { get; set; } = true;
    public bool PreserveHue { get; set; } = true;
    public bool EnhanceLightRegions { get; set; } = true;

    // Kaleido 3 Color E-Ink Default Tuning
    public float PaperWhiteKnee { get; set; } = 0.85f;
    public float InkBlackKnee { get; set; } = 0.18f;
    public float SaturationBoost { get; set; } = 1.35f;
    public float ContrastGamma { get; set; } = 1.04f;
    public float PhotoSensitivity { get; set; } = 0.55f;

    // Frame pacing for E-Ink (default 24 FPS, redraws only on screen changes)
    public int MaxFps { get; set; } = 24;
    public bool DebugMaskOverlay { get; set; } = false;

    public static List<AppInvertRule> CreateDefaultAppRules()
    {
        bool zh = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
        return new()
        {
            new AppInvertRule
            {
                Name = "Antigravity",
                MatchProcesses = "Antigravity",
                Enabled = true,
                RuleMode = AppRuleMode.FollowGlobal
            },
            new AppInvertRule
            {
                Name = zh ? "主流浏览器" : "Web Browsers",
                MatchProcesses = "chrome; msedge; firefox; brave; zen; arc; vivaldi; opera",
                Enabled = true,
                RuleMode = AppRuleMode.SmartAuto
            },
            new AppInvertRule
            {
                Name = "Codex",
                MatchProcesses = "codex",
                Enabled = true,
                RuleMode = AppRuleMode.FollowGlobal
            },
            new AppInvertRule
            {
                Name = zh ? "代码编辑器" : "Code Editors",
                MatchProcesses = "ZCode; Code; Cursor; Windsurf",
                Enabled = true,
                RuleMode = AppRuleMode.FollowGlobal
            },
            new AppInvertRule
            {
                Name = zh ? "命令行与终端" : "Terminals & Shells",
                MatchProcesses = "WindowsTerminal; OpenConsole; conhost; cmd; powershell; pwsh; wezterm-gui; alacritty; mintty",
                Enabled = true,
                RuleMode = AppRuleMode.FollowGlobal
            },
            new AppInvertRule
            {
                Name = zh ? "Zotero 文献阅读" : "Zotero Reader",
                MatchProcesses = "zotero",
                Enabled = true,
                RuleMode = AppRuleMode.SmartAuto
            }
        };
    }

    public FilterMode ResolveWindowMode(ReadOnlySpan<char> processName, ReadOnlySpan<char> className = default)
    {
        if (Mode == FilterMode.Off)
            return FilterMode.Off;

        bool isLegacyConsole = className.SequenceEqual("ConsoleWindowClass") ||
                               className.SequenceEqual("CASCADIA_HOSTING_WINDOW_CLASS");

        var rules = AppRules;
        if (rules != null)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                bool matched = r.MatchesProcess(processName) ||
                               (isLegacyConsole && r.MatchesProcess("cmd"));
                if (!matched)
                    continue;

                if (!r.Enabled)
                    return OnlyInvertListedApps ? FilterMode.Off : Mode;

                return r.RuleMode switch
                {
                    AppRuleMode.FollowGlobal => Mode,
                    AppRuleMode.SmartAuto => FilterMode.SmartAuto,
                    AppRuleMode.ForceInvertAll => FilterMode.ForceInvertAll,
                    AppRuleMode.CrispLightOnly => FilterMode.CrispLightOnly,
                    AppRuleMode.PassthroughOff => FilterMode.Off,
                    _ => Mode
                };
            }
        }

        return OnlyInvertListedApps ? FilterMode.Off : Mode;
    }

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.AppRules = AppRules != null
            ? AppRules.Select(r => r.Clone()).ToList()
            : CreateDefaultAppRules();
        return copy;
    }

    public void ApplyKaleido3Preset()
    {
        PreserveHue = true;
        KeepLightWindows = true;
        ProtectPhotos = true;
        EnhanceLightRegions = true;
        PaperWhiteKnee = 0.85f;
        InkBlackKnee = 0.18f;
        SaturationBoost = 1.35f;
        ContrastGamma = 1.04f;
        PhotoSensitivity = 0.55f;
    }

    public void ApplyCartaMonochromePreset()
    {
        PreserveHue = true;
        KeepLightWindows = true;
        ProtectPhotos = true;
        EnhanceLightRegions = true;
        PaperWhiteKnee = 0.82f;
        InkBlackKnee = 0.22f;
        SaturationBoost = 0.0f; // Pure crisp grayscale for B&W Carta panels
        ContrastGamma = 1.08f;
        PhotoSensitivity = 0.55f;
    }

    private static string GetSettingsPath()
    {
        string baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "settings.local.json");
    }

    public static bool SettingsFileExists() => File.Exists(GetSettingsPath());

    public static AppSettings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded != null)
                {
                    if (loaded.AppRules == null || loaded.AppRules.Count == 0)
                    {
                        loaded.AppRules = CreateDefaultAppRules();
                    }
                    else
                    {
                        bool zh = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
                        foreach (var r in loaded.AppRules)
                        {
                            if (r.Name.StartsWith("浏览器 (", StringComparison.Ordinal)) r.Name = zh ? "主流浏览器" : "Web Browsers";
                            else if (r.Name.StartsWith("CMD / 终端", StringComparison.Ordinal)) r.Name = zh ? "命令行与终端" : "Terminals & Shells";
                            else if (r.Name.StartsWith("Zotero (", StringComparison.Ordinal)) r.Name = zh ? "Zotero 文献阅读" : "Zotero Reader";
                            else if (r.Name.Equals("ZCode / VS Code / Cursor", StringComparison.Ordinal)) r.Name = zh ? "代码编辑器" : "Code Editors";
                        }
                    }
                    return loaded;
                }
            }
        }
        catch
        {
            // Ignore malformed local settings and use defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string path = GetSettingsPath();
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore write errors on read-only media
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
