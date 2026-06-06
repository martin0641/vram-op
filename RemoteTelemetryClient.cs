using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace VramOp;

internal sealed class RemoteTelemetryClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<RemoteTelemetryResult> ReadTelemetryAsync(RemoteHostConfig host, CancellationToken cancellationToken)
    {
        string? observedThumbprint = null;

        using var client = CreateClient(host, thumbprint => observedThumbprint = thumbprint);
        using var response = await client.GetAsync(BuildTelemetryUrl(host), cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return RemoteTelemetryResult.Failed("Unauthorized");
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var telemetry = await JsonSerializer.DeserializeAsync<HostTelemetry>(stream, SerializerOptions, cancellationToken);

        if (telemetry is null)
        {
            return RemoteTelemetryResult.Failed("Empty telemetry payload");
        }

        return RemoteTelemetryResult.Succeeded(telemetry, observedThumbprint);
    }

    public async Task<IReadOnlyList<NetworkInterfaceOption>> ReadNetworkInterfacesAsync(RemoteHostConfig host, CancellationToken cancellationToken)
    {
        using var client = CreateClient(host, _ => { });
        using var response = await client.GetAsync($"{host.BaseUrl}/api/network/interfaces", cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<IReadOnlyList<NetworkInterfaceOption>>(stream, SerializerOptions, cancellationToken)
            ?? [];
    }

    public async Task<KillProcessResponse> KillProcessAsync(RemoteHostConfig host, int processId, CancellationToken cancellationToken)
    {
        return await SendKillRequestAsync(host, $"{host.BaseUrl}/api/processes/{processId}/kill", cancellationToken);
    }

    public async Task<KillProcessResponse> KillParentProcessAsync(RemoteHostConfig host, int processId, CancellationToken cancellationToken)
    {
        return await SendKillRequestAsync(host, $"{host.BaseUrl}/api/processes/{processId}/kill-parent", cancellationToken);
    }

    public async Task<KillProcessResponse> ControlServiceAsync(RemoteHostConfig host, string serviceName, ServiceControlAction action, CancellationToken cancellationToken)
    {
        var actionSegment = action switch
        {
            ServiceControlAction.Start => "start",
            ServiceControlAction.Stop => "stop",
            ServiceControlAction.Enable => "enable",
            ServiceControlAction.Disable => "disable",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
        var encodedServiceName = Uri.EscapeDataString(serviceName);
        return await SendKillRequestAsync(host, $"{host.BaseUrl}/api/services/{encodedServiceName}/{actionSegment}", cancellationToken);
    }

    private async Task<KillProcessResponse> SendKillRequestAsync(RemoteHostConfig host, string url, CancellationToken cancellationToken)
    {
        using var client = CreateClient(host, _ => { });
        using var response = await client.PostAsync(url, content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new KillProcessResponse(false, "Unauthorized");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<KillProcessResponse>(stream, SerializerOptions, cancellationToken)
            ?? new KillProcessResponse(false, "Empty kill response");
    }

    private static HttpClient CreateClient(RemoteHostConfig host, Action<string> onObservedThumbprint)
    {
        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls13,
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                using var cert = new X509Certificate2(certificate);
                var thumbprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
                onObservedThumbprint(thumbprint);

                if (!string.IsNullOrWhiteSpace(host.TrustedCertificateThumbprint))
                {
                    return string.Equals(
                        NormalizeThumbprint(host.TrustedCertificateThumbprint),
                        NormalizeThumbprint(thumbprint),
                        StringComparison.OrdinalIgnoreCase);
                }

                return errors == SslPolicyErrors.None
                    || cert.Subject.Contains("VRAM Vue", StringComparison.OrdinalIgnoreCase)
                    || cert.Subject.Contains("VRAM Op", StringComparison.OrdinalIgnoreCase);
            }
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var rawCredentials = $"{host.Username}:{host.GetPassword()}";
        var encodedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawCredentials));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encodedCredentials);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VRAM-Vue/1.0");
        return client;
    }

    private static string BuildTelemetryUrl(RemoteHostConfig host)
    {
        var builder = new StringBuilder($"{host.BaseUrl}/api/telemetry?networkMode={host.NetworkSelectionMode}");
        foreach (var id in (host.TrackedNetworkInterfaceIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4))
        {
            builder.Append("&nic=");
            builder.Append(Uri.EscapeDataString(id));
        }

        return builder.ToString();
    }

    private static string NormalizeThumbprint(string value) =>
        value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
}

internal sealed record RemoteTelemetryResult(
    bool Success,
    HostTelemetry? Telemetry,
    string? CertificateThumbprint,
    string? ErrorMessage)
{
    public static RemoteTelemetryResult Succeeded(HostTelemetry telemetry, string? certificateThumbprint) =>
        new(true, telemetry, certificateThumbprint, null);

    public static RemoteTelemetryResult Failed(string errorMessage) =>
        new(false, null, null, errorMessage);
}
