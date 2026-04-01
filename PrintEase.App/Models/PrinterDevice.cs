namespace PrintEase.App.Models;

public sealed class PrinterDevice
{
    public required string Name { get; init; }
    public required string PortName { get; init; }
    public bool IsNetwork { get; init; }
    public bool IsOnline { get; init; }
    public bool IsOffline { get; init; }
    public bool IsDefault { get; init; }

    public string ConnectionType => IsNetwork ? "Wi-Fi/LAN" : "USB/Local";
    public string OnlineStatus => IsOnline ? "Online" : (IsOffline ? "Offline" : "Unknown");

    public override string ToString() => Name;
}
