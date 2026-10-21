using System.Text.Json;
using System.Text.Json.Serialization;
using Hanglish.Core;
using Microsoft.Win32;

namespace Hanglish;

sealed class Settings
{
    // HANGLISH_DATA lets tests run against a throwaway folder.
    public static readonly string Folder = Environment.GetEnvironmentVariable("HANGLISH_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hanglish");
    static readonly string FilePath = Path.Combine(Folder, "settings.json");
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public bool Enabled { get; set; } = true;
    public PreviewMode Preview { get; set; } = PreviewMode.Hangul;
    /// <summary>Virtual-key code of the flip key.</summary>
    public int FlipKeyCode { get; set; } = FlipKeys.Default;
    public bool ShownHangulKeyTip { get; set; }
    public bool CaretPreview { get; set; } = true;
    public bool SkipSensitiveFields { get; set; } = true;
    public BubbleStyle BubbleStyle { get; set; } = BubbleStyle.White;

    /// <summary>3: the flip key is any key, 한/영 by default.</summary>
    public int Version { get; set; } = CurrentVersion;
    const int CurrentVersion = 3;
    public List<string> ExcludedApps { get; set; } = new();
    public bool ShownWelcome { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new();
                if (loaded.Version < CurrentVersion || !File.ReadAllText(FilePath).Contains("\"Version\""))
                {
                    loaded.FlipKeyCode = FlipKeys.Default;
                    loaded.Version = CurrentVersion;
                    loaded.Save();
                }
                return loaded;
            }
        }
        catch (Exception e) when (e is JsonException or IOException) { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Hanglish") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Hanglish", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("Hanglish", false);
        }
    }
}
