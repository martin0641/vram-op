using System.Security.Cryptography;
using System.Text.Json;

namespace VramOp;

internal static class SettingsPackage
{
    private const int CurrentVersion = 1;
    private const int KeySizeBytes = 32;
    private const int SaltSizeBytes = 16;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int DefaultIterations = 200_000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Export(AppSettings settings, string password, string path)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("A password is required to export settings.", nameof(password));
        }

        var portable = PortableSettings.From(settings);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(portable, SerializerOptions);

        try
        {
            var envelope = Encrypt(plaintext, password);
            var json = JsonSerializer.Serialize(envelope, SerializerOptions);
            File.WriteAllText(path, json);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static AppSettings Import(string path, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("A password is required to import settings.", nameof(password));
        }

        var json = File.ReadAllText(path);
        var envelope = JsonSerializer.Deserialize<SettingsEnvelope>(json, SerializerOptions)
            ?? throw new InvalidDataException("The settings file is not a valid VRAM Vue export.");

        if (envelope.Version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported settings file version {envelope.Version}.");
        }

        var plaintext = Decrypt(envelope, password);

        try
        {
            var portable = JsonSerializer.Deserialize<PortableSettings>(plaintext, SerializerOptions)
                ?? throw new InvalidDataException("The settings file does not contain usable settings.");
            return portable.ToAppSettings();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static SettingsEnvelope Encrypt(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[plaintext.Length];
        var key = DeriveKey(password, salt, DefaultIterations);

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
            return new SettingsEnvelope
            {
                Version = CurrentVersion,
                Kdf = "PBKDF2-SHA256",
                Cipher = "AES-256-GCM",
                Iterations = DefaultIterations,
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Ciphertext = Convert.ToBase64String(ciphertext)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] Decrypt(SettingsEnvelope envelope, string password)
    {
        if (!string.Equals(envelope.Kdf, "PBKDF2-SHA256", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(envelope.Cipher, "AES-256-GCM", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The settings file uses an unsupported encryption format.");
        }

        var salt = Convert.FromBase64String(envelope.Salt);
        var nonce = Convert.FromBase64String(envelope.Nonce);
        var tag = Convert.FromBase64String(envelope.Tag);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);

        if (salt.Length != SaltSizeBytes || nonce.Length != NonceSizeBytes || tag.Length != TagSizeBytes)
        {
            throw new InvalidDataException("The settings file encryption metadata is invalid.");
        }

        var plaintext = new byte[ciphertext.Length];
        var key = DeriveKey(password, salt, envelope.Iterations);

        try
        {
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            throw new CryptographicException("The settings password is incorrect or the file was modified.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        if (iterations < 100_000)
        {
            throw new InvalidDataException("The settings file uses too few key-derivation iterations.");
        }

        using var deriveBytes = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return deriveBytes.GetBytes(KeySizeBytes);
    }

    private sealed class SettingsEnvelope
    {
        public int Version { get; set; }
        public string Kdf { get; set; } = string.Empty;
        public string Cipher { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public string Salt { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
    }

    private sealed class PortableSettings
    {
        public int Version { get; set; } = CurrentVersion;
        public bool ListenerEnabled { get; set; }
        public int ListenerPort { get; set; } = 54545;
        public string Username { get; set; } = "vram";
        public string Password { get; set; } = string.Empty;
        public int UpdateIntervalMs { get; set; } = 250;
        public int BarSmoothingMs { get; set; } = 500;
        public bool MonitorWindowsStayOnTop { get; set; } = true;
        public int MonitorWindowOpacityPercent { get; set; } = 95;
        public bool ConfirmTaskKills { get; set; } = true;
        public NetworkSelectionMode NetworkSelectionMode { get; set; } = NetworkSelectionMode.Auto;
        public List<string> TrackedNetworkInterfaceIds { get; set; } = [];
        public NetworkRateUnit NetworkRateUnit { get; set; } = NetworkRateUnit.Mbps;
        public int AverageWindowMinutes { get; set; } = 5;
        public Dictionary<string, string> ThemeColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<PortableRemoteHost> RemoteHosts { get; set; } = [];

        public static PortableSettings From(AppSettings settings) =>
            new()
            {
                ListenerEnabled = settings.ListenerEnabled,
                ListenerPort = settings.ListenerPort,
                Username = settings.Username,
                Password = settings.GetPassword(),
                UpdateIntervalMs = settings.UpdateIntervalMs,
                BarSmoothingMs = settings.BarSmoothingMs,
                MonitorWindowsStayOnTop = settings.MonitorWindowsStayOnTop,
                MonitorWindowOpacityPercent = settings.MonitorWindowOpacityPercent,
                ConfirmTaskKills = settings.ConfirmTaskKills,
                NetworkSelectionMode = settings.NetworkSelectionMode,
                TrackedNetworkInterfaceIds = settings.TrackedNetworkInterfaceIds.ToList(),
                NetworkRateUnit = settings.NetworkRateUnit,
                AverageWindowMinutes = settings.AverageWindowMinutes,
                ThemeColors = new Dictionary<string, string>(settings.ThemeColors, StringComparer.OrdinalIgnoreCase),
                RemoteHosts = settings.RemoteHosts.Select(PortableRemoteHost.From).ToList()
            };

        public AppSettings ToAppSettings()
        {
            var settings = new AppSettings
            {
                ListenerEnabled = ListenerEnabled,
                ListenerPort = ListenerPort,
                Username = Username,
                UpdateIntervalMs = UpdateIntervalMs,
                BarSmoothingMs = BarSmoothingMs,
                MonitorWindowsStayOnTop = MonitorWindowsStayOnTop,
                MonitorWindowOpacityPercent = MonitorWindowOpacityPercent,
                ConfirmTaskKills = ConfirmTaskKills,
                NetworkSelectionMode = NetworkSelectionMode,
                TrackedNetworkInterfaceIds = TrackedNetworkInterfaceIds ?? [],
                NetworkRateUnit = NetworkRateUnit,
                AverageWindowMinutes = AverageWindowMinutes,
                ThemeColors = new Dictionary<string, string>(ThemeColors, StringComparer.OrdinalIgnoreCase),
                RemoteHosts = RemoteHosts.Select(item => item.ToRemoteHostConfig()).ToList()
            };
            settings.SetPassword(Password);
            return settings;
        }
    }

    private sealed class PortableRemoteHost
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 54545;
        public string Username { get; set; } = "vram";
        public string Password { get; set; } = string.Empty;
        public string TrustedCertificateThumbprint { get; set; } = string.Empty;
        public NetworkSelectionMode NetworkSelectionMode { get; set; } = NetworkSelectionMode.Auto;
        public List<string> TrackedNetworkInterfaceIds { get; set; } = [];

        public static PortableRemoteHost From(RemoteHostConfig remote) =>
            new()
            {
                Id = remote.Id,
                Name = remote.Name,
                Host = remote.Host,
                Port = remote.Port,
                Username = remote.Username,
                Password = remote.GetPassword(),
                TrustedCertificateThumbprint = remote.TrustedCertificateThumbprint,
                NetworkSelectionMode = remote.NetworkSelectionMode,
                TrackedNetworkInterfaceIds = remote.TrackedNetworkInterfaceIds?.ToList() ?? []
            };

        public RemoteHostConfig ToRemoteHostConfig()
        {
            var remote = new RemoteHostConfig
            {
                Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
                Name = Name,
                Host = Host,
                Port = Port,
                Username = Username,
                TrustedCertificateThumbprint = TrustedCertificateThumbprint,
                NetworkSelectionMode = NetworkSelectionMode,
                TrackedNetworkInterfaceIds = TrackedNetworkInterfaceIds ?? []
            };
            remote.SetPassword(Password);
            return remote;
        }
    }
}
