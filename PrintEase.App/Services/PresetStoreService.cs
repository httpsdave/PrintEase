using System.IO;
using System.Text.Json;
using PrintEase.App.Models;

namespace PrintEase.App.Services;

public sealed class PresetStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public PresetStoreService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintEase");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "presets.json");
    }

    public IReadOnlyList<PrintPreset> GetPresets(string printerName)
    {
        var store = ReadStore();
        return store.TryGetValue(printerName, out var presets)
            ? presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<PrintPreset>();
    }

    public void SavePreset(string printerName, PrintPreset preset)
    {
        var store = ReadStore();
        if (!store.TryGetValue(printerName, out var presets))
        {
            presets = new List<PrintPreset>();
            store[printerName] = presets;
        }

        var existingIndex = presets.FindIndex(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            presets[existingIndex] = preset;
        }
        else
        {
            presets.Add(preset);
        }

        WriteStore(store);
    }

    public void DeletePreset(string printerName, string presetName)
    {
        var store = ReadStore();
        if (!store.TryGetValue(printerName, out var presets))
        {
            return;
        }

        presets.RemoveAll(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));

        if (presets.Count == 0)
        {
            store.Remove(printerName);
        }

        WriteStore(store);
    }

    private Dictionary<string, List<PrintPreset>> ReadStore()
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, List<PrintPreset>>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, List<PrintPreset>>(StringComparer.OrdinalIgnoreCase);
        }

        var store = JsonSerializer.Deserialize<Dictionary<string, List<PrintPreset>>>(json, JsonOptions);
        return store is null
            ? new Dictionary<string, List<PrintPreset>>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, List<PrintPreset>>(store, StringComparer.OrdinalIgnoreCase);
    }

    private void WriteStore(Dictionary<string, List<PrintPreset>> store)
    {
        var json = JsonSerializer.Serialize(store, JsonOptions);
        File.WriteAllText(_path, json);
    }
}
