using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VramOp;

internal sealed class AppSettings
{
    public bool ListenerEnabled { get; set; }
    public int ListenerPort { get; set; } = 54545;
    public string Username { get; set; } = "vram";
    public string ProtectedPassword { get; set; } = string.Empty;
    public int UpdateIntervalMs { get; set; } = 250;
    public int BarSmoothingMs { get; set; } = 500;
    public bool MonitorWindowsStayOnTop { get; set; } = true;
    public int MonitorWindowOpacityPercent { get; set; } = 95;
    public bool AutoUpdateEnabled { get; set; } = true;
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
    public bool ConfirmTaskKills { get; set; } = true;
    public NetworkSelectionMode NetworkSelectionMode { get; set; } = NetworkSelectionMode.Auto;
    public List<string> TrackedNetworkInterfaceIds { get; set; } = [];
    public NetworkRateUnit NetworkRateUnit { get; set; } = NetworkRateUnit.Mbps;
    public int AverageWindowMinutes { get; set; } = 5;
    public List<RemoteHostConfig> RemoteHosts { get; set; } = [];
    public Dictionary<string, string> ThemeColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetPassword() => SettingsProtector.Unprotect(ProtectedPassword);

    public void SetPassword(string password)
    {
        ProtectedPassword = SettingsProtector.Protect(password);
    }
}

internal enum NetworkSelectionMode
{
    Auto,
    Manual
}

internal enum NetworkRateUnit
{
    Mbps,
    MBps,
    Gbps,
    GBps,
    Mibps,
    MiBps,
    Gibps,
    GiBps
}

internal sealed class RemoteHostConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 54545;
    public string Username { get; set; } = "vram";
    public string ProtectedPassword { get; set; } = string.Empty;
    public string TrustedCertificateThumbprint { get; set; } = string.Empty;
    public NetworkSelectionMode NetworkSelectionMode { get; set; } = NetworkSelectionMode.Auto;
    public List<string> TrackedNetworkInterfaceIds { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;
    public string BaseUrl => $"https://{Host}:{Port}";
    public string GetPassword() => SettingsProtector.Unprotect(ProtectedPassword);

    public void SetPassword(string password)
    {
        ProtectedPassword = SettingsProtector.Protect(password);
    }
}

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VramOp");

    public static string SettingsPath { get; } = Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(SettingsPath, json);
    }
}

internal static class SettingsProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VRAM-OP-v1");

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(protectedValue);
            var clearBytes = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}
