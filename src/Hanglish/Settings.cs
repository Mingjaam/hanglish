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
    /// <summary>Virtual-key code of the key that toggles the preview mode, or 0 for none.</summary>
    public int PreviewKeyCode { get; set; } = FlipKeys.PreviewDefault;
    public bool ShownHangulKeyTip { get; set; }
    public bool CaretPreview { get; set; } = true;
    public bool SkipSensitiveFields { get; set; } = true;
    public BubbleStyle BubbleStyle { get; set; } = BubbleStyle.White;

    /// <summary>
    /// 4: the default flip key moved from 한/영 to right Ctrl (habit presses 한/영 before Korean).
    /// 5: 한/영 became the preview toggle key.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;
    const int CurrentVersion = 5;
    public List<string> ExcludedApps { get; set; } = new();
    public bool ShownWelcome { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<Settings>(text, Json) ?? new();
                int from = text.Contains("\"Version\"") ? loaded.Version : 0;
                bool changed = from < CurrentVersion;
                // Earlier defaults (한/영 in 0.3.0, right Ctrl before) become the new default; a key the
                // user picked on purpose stays.
                if (from < 3 || from < 4 && loaded.FlipKeyCode == Native.VK_HANGUL) loaded.FlipKeyCode = FlipKeys.Default;
                if (from < 5) loaded.PreviewKeyCode = FlipKeys.PreviewDefault;
                // The flip key wins a clash: someone who made 한/영 their flip key keeps it.
                if (FlipKeys.Same(loaded.PreviewKeyCode, loaded.FlipKeyCode)) { loaded.PreviewKeyCode = FlipKeys.None; changed = true; }
                loaded.Version = CurrentVersion;
                if (changed) loaded.Save();
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
