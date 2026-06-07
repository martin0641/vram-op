using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VramOp;

internal sealed class AppUpdateInfo
{
    public required Version Version { get; init; }
    public required string TagName { get; init; }
    public required string ReleaseUrl { get; init; }
    public required string AssetName { get; init; }
    public required Uri AssetDownloadUrl { get; init; }
}

internal static class AppUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/martin0641/vram-op/releases/latest";
    private const string UserAgent = "VRAM-Vue-Updater";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Version CurrentVersion =>
        Normalize(typeof(AppUpdateService).Assembly.GetName().Version ?? Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

    public static string CurrentVersionText => FormatVersion(CurrentVersion);

    public static async Task<AppUpdateInfo?> CheckLatestAsync(CancellationToken cancellationToken)
    {
        using var http = CreateHttpClient();
        await using var stream = await http.GetStreamAsync(LatestReleaseUrl, cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, SerializerOptions, cancellationToken);
        if (release is null || release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.TagName))
        {
            return null;
        }

        var latestVersion = ParseVersion(release.TagName);
        if (latestVersion <= CurrentVersion)
        {
            return null;
        }

        var asset = release.Assets
            .FirstOrDefault(item => item.Name.EndsWith("-win-x64.msi", StringComparison.OrdinalIgnoreCase)
                && item.Name.Contains($"v{FormatVersion(latestVersion)}", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(item => item.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            return null;
        }

        return new AppUpdateInfo
        {
            Version = latestVersion,
            TagName = release.TagName,
            ReleaseUrl = release.HtmlUrl,
            AssetName = asset.Name,
            AssetDownloadUrl = new Uri(asset.BrowserDownloadUrl)
        };
    }

    public static async Task<string> DownloadInstallerAsync(AppUpdateInfo update, CancellationToken cancellationToken)
    {
        using var http = CreateHttpClient();
        using var response = await http.GetAsync(update.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var updateDirectory = Path.Combine(SettingsStore.SettingsDirectory, "updates");
        Directory.CreateDirectory(updateDirectory);
        var installerPath = Path.Combine(updateDirectory, update.AssetName);
        var temporaryPath = installerPath + ".download";

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        if (File.Exists(installerPath))
        {
            File.Delete(installerPath);
        }

        File.Move(temporaryPath, installerPath);
        return installerPath;
    }

    public static void LaunchDeferredInstaller(string installerPath, int processId)
    {
        var scriptPath = Path.Combine(
            SettingsStore.SettingsDirectory,
            "updates",
            $"install-vram-vue-{Guid.NewGuid():N}.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);

        var escapedInstallerPath = installerPath.Replace("'", "''", StringComparison.Ordinal);
        var script = string.Join(Environment.NewLine,
            "$ErrorActionPreference = 'Stop'",
            $"$processId = {processId}",
            $"$installerPath = '{escapedInstallerPath}'",
            "try {",
            "    Wait-Process -Id $processId -ErrorAction SilentlyContinue",
            "}",
            "catch {",
            "}",
            "Start-Sleep -Milliseconds 800",
            "Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i', $installerPath, '/passive', '/norestart') -Verb RunAs -Wait",
            string.Empty);
        File.WriteAllText(scriptPath, script);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(startInfo);
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static Version ParseVersion(string tagName)
    {
        var text = tagName.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            text = text[1..];
        }

        return Version.TryParse(text, out var version)
            ? Normalize(version)
            : new Version(0, 0);
    }

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    private static string FormatVersion(Version version) =>
        version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
