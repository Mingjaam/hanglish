using System.Text.Json;

namespace Hanglish.Core;

public enum CorrectionReach { AfterSameWord, Everywhere }

public sealed record Correction(string Raw, string Before, string Preferred, CorrectionReach Reach);

/// <summary>
/// Remembers the user's flips. A flip applies only after the same preceding word,
/// unless the same choice was made after two different words, which makes it apply everywhere.
/// </summary>
public sealed class CorrectionStore
{
    readonly string? path;
    readonly List<Correction> entries = new();
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public CorrectionStore(string? path)
    {
        this.path = path;
        if (path is null || !File.Exists(path)) return;
        try { entries = JsonSerializer.Deserialize<List<Correction>>(File.ReadAllText(path), Json) ?? entries; }
        catch (JsonException)
        {
            // Version 0.1 stored context-free flips; set them aside instead of applying them everywhere.
            File.Move(path, path + ".old", overwrite: true);
        }
    }

    public int Count => entries.Count;

    public IReadOnlyList<Correction> Entries => entries;

    /// <param name="before">The word right before this one in the same field, or "".</param>
    public string? Lookup(string raw, string before) =>
        (entries.LastOrDefault(e => e.Raw == raw && e.Reach == CorrectionReach.Everywhere)
         ?? entries.LastOrDefault(e => e.Raw == raw && e.Before == before))?.Preferred;

    public void Remember(string raw, string before, string preferred)
    {
        entries.RemoveAll(e => e.Raw == raw && (e.Before == before || e.Reach == CorrectionReach.Everywhere));
        bool elsewhere = entries.Any(e => e.Raw == raw && e.Preferred == preferred);
        if (elsewhere) entries.RemoveAll(e => e.Raw == raw);
        entries.Add(new Correction(raw, elsewhere ? "" : before, preferred,
            elsewhere ? CorrectionReach.Everywhere : CorrectionReach.AfterSameWord));
        Save();
    }

    public void Forget(string raw, string before)
    {
        if (entries.RemoveAll(e => e.Raw == raw && (e.Before == before || e.Reach == CorrectionReach.Everywhere)) > 0)
            Save();
    }

    public void Remove(Correction entry) { if (entries.Remove(entry)) Save(); }

    public void Clear() { entries.Clear(); Save(); }

    void Save()
    {
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, Json));
    }
}
