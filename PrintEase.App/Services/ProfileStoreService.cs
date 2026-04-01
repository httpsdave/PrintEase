using System.IO;
using System.Text.Json;
using PrintEase.App.Models;

namespace PrintEase.App.Services;

public sealed class ProfileStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _profilesPath;

    public ProfileStoreService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintEase");
        Directory.CreateDirectory(root);
        _profilesPath = Path.Combine(root, "profiles.json");
    }

    public PrinterProfile? LoadProfile(string printerName)
    {
        var profiles = ReadAllProfiles();
        return profiles.TryGetValue(printerName, out var profile) ? profile : null;
    }

    public void SaveProfile(PrinterProfile profile)
    {
        var profiles = ReadAllProfiles();
        profiles[profile.PrinterName] = profile;

        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        File.WriteAllText(_profilesPath, json);
    }

    private Dictionary<string, PrinterProfile> ReadAllProfiles()
    {
        if (!File.Exists(_profilesPath))
        {
            return new Dictionary<string, PrinterProfile>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(_profilesPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, PrinterProfile>(StringComparer.OrdinalIgnoreCase);
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, PrinterProfile>>(json, JsonOptions);
        return parsed is null
            ? new Dictionary<string, PrinterProfile>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, PrinterProfile>(parsed, StringComparer.OrdinalIgnoreCase);
    }
}
