using System.IO;
using System.Text;
using System.Text.Json;

namespace PrintEase.App.Services;

public sealed class DiagnosticsService
{
    private readonly string _logDirectory;
    public string StorageRoot { get; }

    public DiagnosticsService()
    {
        StorageRoot = ResolveNonWindowsDriveRoot();
        if (string.IsNullOrWhiteSpace(StorageRoot))
        {
            _logDirectory = string.Empty;
            return;
        }

        _logDirectory = Path.Combine(StorageRoot, "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message)
    {
        WriteLog("INFO", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        var details = exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}";
        WriteLog("ERROR", details);
    }

    public string ExportSnapshot(object snapshot)
    {
        if (string.IsNullOrWhiteSpace(_logDirectory))
        {
            throw new InvalidOperationException("No non-Windows drive available for diagnostics export. Connect or enable another drive first.");
        }

        var timestamp = DateTime.Now;
        string snapshotPath = Path.Combine(_logDirectory, $"snapshot-{timestamp:yyyyMMdd-HHmmss}.json");
        string textPath = Path.Combine(_logDirectory, $"snapshot-{timestamp:yyyyMMdd-HHmmss}.txt");

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(snapshotPath, json, Encoding.UTF8);

        var text = new StringBuilder()
            .AppendLine("PrintEase Diagnostics Snapshot")
            .AppendLine($"Generated: {timestamp:F}")
            .AppendLine()
            .AppendLine(json)
            .ToString();
        File.WriteAllText(textPath, text, Encoding.UTF8);

        Info($"Snapshot exported: {snapshotPath}");
        return snapshotPath;
    }

    private void WriteLog(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(_logDirectory))
        {
            return;
        }

        var filePath = Path.Combine(_logDirectory, $"printease-{DateTime.Now:yyyyMMdd}.log");
        var line = $"[{DateTime.Now:O}] [{level}] {message}{Environment.NewLine}";
        File.AppendAllText(filePath, line, Encoding.UTF8);
    }

    private static string ResolveNonWindowsDriveRoot()
    {
        var windowsRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

        var candidate = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Where(d => !string.Equals(d.RootDirectory.FullName, windowsRoot, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.AvailableFreeSpace)
            .FirstOrDefault();

        if (candidate is null)
        {
            return string.Empty;
        }

        var root = Path.Combine(candidate.RootDirectory.FullName, "PrintEaseData");
        Directory.CreateDirectory(root);
        return root;
    }
}
