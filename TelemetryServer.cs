using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace VramOp;

internal sealed class TelemetryServer : IAsyncDisposable
{
    private readonly SystemTelemetryCollector _collector;
    private WebApplication? _app;
    private CancellationTokenSource? _cts;

    public TelemetryServer(SystemTelemetryCollector collector)
    {
        _collector = collector;
    }

    public bool IsRunning => _app is not null;
    public string Status { get; private set; } = "Listener stopped";
    public string CertificateThumbprint { get; private set; } = string.Empty;

    public async Task StartAsync(AppSettings settings)
    {
        await StopAsync();
        _collector.ApplySettings(settings);

        if (!settings.ListenerEnabled)
        {
            Status = "Listener disabled";
            return;
        }

        var password = settings.GetPassword();
        if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrEmpty(password))
        {
            Status = "Listener disabled until username and password are set";
            return;
        }

        var certificate = CertificateManager.GetOrCreateCertificate();
        CertificateThumbprint = CertificateManager.GetSha256Thumbprint(certificate);
        _cts = new CancellationTokenSource();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = []
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(settings.ListenerPort, listenOptions =>
            {
                listenOptions.UseHttps(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = certificate,
                    SslProtocols = SslProtocols.Tls13,
                    ClientCertificateMode = ClientCertificateMode.NoCertificate
                });
            });
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (!IsAuthorized(context.Request, settings.Username, password))
            {
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"VRAM Vue\"";
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized", _cts.Token);
                return;
            }

            await next();
        });

        app.MapGet("/api/telemetry", () => Results.Json(_collector.Read()));
        app.MapPost("/api/processes/{processId:int}/kill", (int processId) => Results.Json(_collector.KillProcess(processId)));
        app.MapPost("/api/processes/{processId:int}/kill-parent", (int processId) => Results.Json(_collector.KillParentProcess(processId)));
        app.MapPost("/api/services/{serviceName}/start", (string serviceName) => Results.Json(_collector.ControlService(serviceName, ServiceControlAction.Start)));
        app.MapPost("/api/services/{serviceName}/stop", (string serviceName) => Results.Json(_collector.ControlService(serviceName, ServiceControlAction.Stop)));
        app.MapPost("/api/services/{serviceName}/enable", (string serviceName) => Results.Json(_collector.ControlService(serviceName, ServiceControlAction.Enable)));
        app.MapPost("/api/services/{serviceName}/disable", (string serviceName) => Results.Json(_collector.ControlService(serviceName, ServiceControlAction.Disable)));

        await app.StartAsync(_cts.Token);
        _app = app;
        Status = $"Listening on https://+:{settings.ListenerPort} with TLS 1.3";
    }

    public async Task StopAsync()
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            _cts?.Cancel();
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _app.StopAsync(stopCts.Token);
            await _app.DisposeAsync();
        }
        catch
        {
            // The listener is being torn down; stale sockets are harmless here.
        }
        finally
        {
            _app = null;
            _cts?.Dispose();
            _cts = null;
            Status = "Listener stopped";
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private static bool IsAuthorized(HttpRequest request, string username, string password)
    {
        if (!request.Headers.TryGetValue("Authorization", out var values))
        {
            return false;
        }

        var authorization = values.ToString();
        if (!authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var encoded = authorization["Basic ".Length..].Trim();
            var clear = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var separator = clear.IndexOf(':');
            if (separator < 0)
            {
                return false;
            }

            var suppliedUsername = clear[..separator];
            var suppliedPassword = clear[(separator + 1)..];
            return FixedTimeEquals(suppliedUsername, username)
                && FixedTimeEquals(suppliedPassword, password);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
