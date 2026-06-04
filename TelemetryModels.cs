namespace VramOp;

internal sealed record HostTelemetry(
    string HostId,
    string HostName,
    DateTimeOffset CapturedAt,
    double CpuPercent,
    double GpuPercent,
    long RamUsedBytes,
    long RamTotalBytes,
    long VramUsedBytes,
    long VramTotalBytes,
    long SharedGpuMemoryBytes,
    IReadOnlyList<GpuAdapterInfo> GpuAdapters,
    IReadOnlyList<GpuProcessInfo> TopGpuProcesses,
    string? ErrorMessage);

internal sealed record GpuAdapterInfo(
    string Name,
    long VramTotalBytes,
    string DriverVersion);

internal sealed record GpuProcessInfo(
    int ProcessId,
    string ProcessName,
    long LocalVramBytes,
    long DedicatedCounterBytes,
    long SharedBytes,
    long CommittedBytes,
    string WindowTitle,
    string ExecutablePath,
    string Notes,
    bool CanKill,
    string RestartBehavior,
    string ServiceName,
    string ServiceDisplayName,
    string ServiceState,
    string ServiceStartMode,
    int ServiceCount,
    int? ParentProcessId,
    string ParentProcessName);

internal enum ServiceControlAction
{
    Start,
    Stop,
    Enable,
    Disable
}

internal sealed record KillProcessRequest(int ProcessId);

internal sealed record KillProcessResponse(bool Success, string Message);

internal sealed class HostSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public bool IsLocal { get; init; }
    public string Endpoint { get; set; } = string.Empty;
    public HostTelemetry? Telemetry { get; set; }
    public string Status { get; set; } = "Waiting";
    public DateTimeOffset LastSeen { get; set; }
    public string TrustedCertificateThumbprint { get; set; } = string.Empty;

    public double CpuPercent => Telemetry?.CpuPercent ?? 0;
    public double GpuPercent => Telemetry?.GpuPercent ?? 0;
    public long RamUsedBytes => Telemetry?.RamUsedBytes ?? 0;
    public long RamTotalBytes => Telemetry?.RamTotalBytes ?? 0;
    public long VramUsedBytes => Telemetry?.VramUsedBytes ?? 0;
    public long VramTotalBytes => Telemetry?.VramTotalBytes ?? 0;
    public IReadOnlyList<GpuProcessInfo> TopGpuProcesses => Telemetry?.TopGpuProcesses ?? Array.Empty<GpuProcessInfo>();
}
