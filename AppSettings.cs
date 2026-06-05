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
    public bool ConfirmTaskKills { get; set; } = true;
    public List<RemoteHostConfig> RemoteHosts { get; set; } = [];

    public string GetPassword() => SettingsProtector.Unprotect(ProtectedPassword);

    public void SetPassword(string password)
    {
        ProtectedPassword = SettingsProtector.Protect(password);
    }
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
