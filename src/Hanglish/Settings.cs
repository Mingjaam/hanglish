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

    /// <summary>4: the default flip key moved from 한/영 to right Ctrl (habit presses 한/영 before Korean).</summary>
    public int Version { get; set; } = CurrentVersion;
    const int CurrentVersion = 4;
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
                    // Earlier defaults (한/영 in 0.3.0, right Ctrl before) become the new default; a key the
                    // user picked on purpose stays.
                    if (loaded.Version < 3 || loaded.FlipKeyCode == Native.VK_HANGUL) loaded.FlipKeyCode = FlipKeys.Default;
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
