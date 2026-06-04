using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace VramOp;

internal sealed class SystemTelemetryCollector : IDisposable
{
    private readonly GpuProcessMemoryReader _gpuMemoryReader = new();
    private readonly PerformanceCounter? _cpuCounter;
    private readonly List<GpuAdapterInfo> _adapters;
    private readonly string _hostId;
    private bool _disposed;

    public SystemTelemetryCollector()
    {
        _hostId = $"{Environment.MachineName}-{Environment.UserName}";
        _adapters = ReadGpuAdapters();

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _cpuCounter.NextValue();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            _cpuCounter = null;
        }
    }

    public HostTelemetry Read()
    {
        var gpuSnapshot = _gpuMemoryReader.Read();
        var restartIndex = ProcessRestartIndex.Read();
        var memory = ReadMemoryStatus();
        var gpuPercent = ReadGpuUtilization();
        var cpuPercent = ReadCpuPercent();
        var topProcesses = gpuSnapshot.Rows
            .OrderByDescending(row => row.LocalBytes)
            .ThenByDescending(row => row.DedicatedBytes)
            .Take(10)
            .Select(row =>
            {
                var parent = restartIndex.GetParent(row.ProcessId);
                var service = restartIndex.GetPrimaryService(row.ProcessId);
                return new GpuProcessInfo(
                    row.ProcessId,
                    FormatProcessName(row),
                    row.LocalBytes,
                    row.DedicatedBytes,
                    row.SharedBytes,
                    row.NonLocalBytes,
                    row.TotalCommittedBytes,
                    row.WindowTitle,
                    row.ExecutablePath,
                    row.Notes,
                    row.CanKill,
                    restartIndex.Describe(row),
                    service?.Name ?? string.Empty,
                    service?.DisplayName ?? string.Empty,
                    service?.State ?? string.Empty,
                    service?.StartMode ?? string.Empty,
                    restartIndex.GetServiceCount(row.ProcessId),
                    parent?.ProcessId,
                    parent?.Name ?? string.Empty);
            })
            .ToArray();

        return new HostTelemetry(
            _hostId,
            Environment.MachineName,
            DateTimeOffset.Now,
            cpuPercent,
            gpuPercent,
            memory.UsedBytes,
            memory.TotalBytes,
            gpuSnapshot.AdapterMemory.DedicatedBytes,
            _adapters.Sum(adapter => adapter.VramTotalBytes),
            gpuSnapshot.AdapterMemory.SharedBytes,
            _adapters,
            topProcesses,
            gpuSnapshot.ErrorMessage);
    }

    private static string FormatProcessName(GpuProcessMemoryRow row)
    {
        if (row.IsProtected)
        {
            return $"{row.ProcessName} (Protected)";
        }

        return row.ProcessName;
    }

    public KillProcessResponse KillProcess(int processId)
    {
        var snapshot = _gpuMemoryReader.Read();
        var row = snapshot.Rows.FirstOrDefault(item => item.ProcessId == processId);

        if (row is null)
        {
            return new KillProcessResponse(false, $"PID {processId} is not currently using GPU memory.");
        }

        if (!row.CanKill)
        {
            return new KillProcessResponse(false, $"{row.ProcessName} ({processId}) is protected or not killable.");
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: false);
            return new KillProcessResponse(true, $"Killed {row.ProcessName} ({processId}).");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return new KillProcessResponse(false, $"Could not kill PID {processId}: {ex.Message}");
        }
    }

    public KillProcessResponse KillParentProcess(int processId)
    {
        var restartIndex = ProcessRestartIndex.Read();
        var parent = restartIndex.GetParent(processId);

        if (parent is null)
        {
            return new KillProcessResponse(false, $"PID {processId} does not have a killable parent process.");
        }

        if (!IsKillablePid(parent.ProcessId))
        {
            return new KillProcessResponse(false, $"{parent.Name} ({parent.ProcessId}) is a protected/system parent process.");
        }

        try
        {
            using var process = Process.GetProcessById(parent.ProcessId);
            process.Kill(entireProcessTree: false);
            return new KillProcessResponse(true, $"Killed parent {parent.Name} ({parent.ProcessId}) for PID {processId}.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return new KillProcessResponse(false, $"Could not kill parent PID {parent.ProcessId}: {ex.Message}");
        }
    }

    private static bool IsKillablePid(int processId) =>
        processId > 4 && processId != Environment.ProcessId;

    public KillProcessResponse ControlService(string serviceName, ServiceControlAction action)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return new KillProcessResponse(false, "No Windows service is associated with that process.");
        }

        try
        {
            using var service = GetServiceObject(serviceName);
            var displayName = service["DisplayName"]?.ToString() ?? serviceName;
            var resultCode = action switch
            {
                ServiceControlAction.Start => InvokeServiceMethod(service, "StartService"),
                ServiceControlAction.Stop => InvokeServiceMethod(service, "StopService"),
                ServiceControlAction.Enable => InvokeServiceMethod(service, "ChangeStartMode", "Manual"),
                ServiceControlAction.Disable => InvokeServiceMethod(service, "ChangeStartMode", "Disabled"),
                _ => 1U
            };

            if (resultCode == 0)
            {
                return new KillProcessResponse(true, $"{FormatServiceAction(action)} succeeded for {displayName}.");
            }

            if (action == ServiceControlAction.Start && resultCode == 10)
            {
                return new KillProcessResponse(true, $"{displayName} is already running.");
            }

            if (action == ServiceControlAction.Stop && resultCode == 10)
            {
                return new KillProcessResponse(true, $"{displayName} is already stopped.");
            }

            return new KillProcessResponse(false, $"{FormatServiceAction(action)} failed for {displayName}: {FormatServiceReturnCode(resultCode)}");
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            return new KillProcessResponse(false, $"Could not control service {serviceName}: {ex.Message}");
        }
    }

    private static ManagementObject GetServiceObject(string serviceName)
    {
        var escaped = serviceName.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        var service = new ManagementObject($@"Win32_Service.Name=""{escaped}""");
        service.Get();
        return service;
    }

    private static uint InvokeServiceMethod(ManagementObject service, string methodName)
    {
        var result = service.InvokeMethod(methodName, Array.Empty<object>());
        return Convert.ToUInt32(result ?? 1);
    }

    private static uint InvokeServiceMethod(ManagementObject service, string methodName, string argument)
    {
        var result = service.InvokeMethod(methodName, new object[] { argument });
        return Convert.ToUInt32(result ?? 1);
    }

    private static string FormatServiceAction(ServiceControlAction action) =>
        action switch
        {
            ServiceControlAction.Start => "Start service",
            ServiceControlAction.Stop => "Stop service",
            ServiceControlAction.Enable => "Enable service",
            ServiceControlAction.Disable => "Disable service",
            _ => "Service action"
        };

    private static string FormatServiceReturnCode(uint code) =>
        code switch
        {
            1 => "not supported",
            2 => "access denied",
            3 => "dependent services are running",
            4 => "invalid service control",
            5 => "the service cannot accept the requested control",
            6 => "the service is inactive",
            7 => "service request timeout",
            8 => "unknown failure",
            9 => "path not found",
            10 => "already in requested state",
            11 => "service database locked",
            12 => "dependency deleted",
            13 => "dependency failure",
            14 => "service disabled",
            15 => "service logon failure",
            16 => "service marked for deletion",
            17 => "service has no execution thread",
            18 => "circular dependency",
            19 => "duplicate service name",
            20 => "invalid service name",
            21 => "invalid parameter",
            22 => "invalid service account",
            23 => "service exists",
            24 => "service already paused",
            _ => $"Windows service return code {code}"
        };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cpuCounter?.Dispose();
        _disposed = true;
    }

    private double ReadCpuPercent()
    {
        try
        {
            return ClampPercent(_cpuCounter?.NextValue() ?? 0);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return 0;
        }
    }

    private static double ReadGpuUtilization()
    {
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                return 0;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            var instances = category.GetInstanceNames();
            double total = 0;

            foreach (var instance in instances)
            {
                if (!instance.Contains("engtype_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var counter = new PerformanceCounter("GPU Engine", "% Utilization", instance, readOnly: true);
                    total += counter.NextValue();
                }
                catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
                {
                    // Some GPU engine instances disappear while counters are being read.
                }
            }

            return ClampPercent(total);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return 0;
        }
    }

    private static List<GpuAdapterInfo> ReadGpuAdapters()
    {
        var adapters = new List<GpuAdapterInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
            foreach (ManagementObject adapter in searcher.Get())
            {
                var name = adapter["Name"]?.ToString() ?? "GPU";
                var driverVersion = adapter["DriverVersion"]?.ToString() ?? string.Empty;
                var rawRam = adapter["AdapterRAM"];
                var ramBytes = rawRam switch
                {
                    uint value => value,
                    int value when value > 0 => value,
                    long value when value > 0 => value,
                    ulong value => value > long.MaxValue ? long.MaxValue : (long)value,
                    _ => 0L
                };

                if (ramBytes > 0)
                {
                    adapters.Add(new GpuAdapterInfo(name, ramBytes, driverVersion));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            // Adapter totals are best effort; per-adapter capacity is not exposed by the GPU perf counters.
        }

        return adapters;
    }

    private static MemoryStatus ReadMemoryStatus()
    {
        var status = new MemoryStatusEx();
        status.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();

        if (!GlobalMemoryStatusEx(ref status))
        {
            return new MemoryStatus(0, 0);
        }

        var total = status.ullTotalPhys > long.MaxValue ? long.MaxValue : (long)status.ullTotalPhys;
        var available = status.ullAvailPhys > long.MaxValue ? long.MaxValue : (long)status.ullAvailPhys;
        return new MemoryStatus(total, Math.Max(0, total - available));
    }

    private static double ClampPercent(double value) => Math.Min(100, Math.Max(0, value));

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private readonly record struct MemoryStatus(long TotalBytes, long UsedBytes);

    private sealed class ProcessRestartIndex
    {
        private readonly IReadOnlyDictionary<int, List<ServiceProcessInfo>> _servicesByPid;
        private readonly IReadOnlyDictionary<int, ProcessParentInfo> _processesByPid;

        private ProcessRestartIndex(
            IReadOnlyDictionary<int, List<ServiceProcessInfo>> servicesByPid,
            IReadOnlyDictionary<int, ProcessParentInfo> processesByPid)
        {
            _servicesByPid = servicesByPid;
            _processesByPid = processesByPid;
        }

        public static ProcessRestartIndex Read()
        {
            return new ProcessRestartIndex(ReadServicesByPid(), ReadProcessParents());
        }

        public string Describe(GpuProcessMemoryRow row)
        {
            if (_servicesByPid.TryGetValue(row.ProcessId, out var services) && services.Count > 0)
            {
                var service = SelectPrimaryService(services);
                var extra = services.Count > 1 ? $" +{services.Count - 1}" : string.Empty;
                return $"Yes - SCM: {service.DisplayName}{extra} ({service.State}, {service.StartMode})";
            }

            if (row.IsProtected)
            {
                return "System managed";
            }

            if (_processesByPid.TryGetValue(row.ProcessId, out var process)
                && process.ParentProcessId > 0
                && _processesByPid.TryGetValue(process.ParentProcessId, out var parent))
            {
                return $"Maybe - {parent.Name} ({parent.ProcessId})";
            }

            return "Unknown";
        }

        public ServiceProcessInfo? GetPrimaryService(int processId)
        {
            if (!_servicesByPid.TryGetValue(processId, out var services) || services.Count == 0)
            {
                return null;
            }

            return SelectPrimaryService(services);
        }

        public int GetServiceCount(int processId) =>
            _servicesByPid.TryGetValue(processId, out var services) ? services.Count : 0;

        public ProcessParentInfo? GetParent(int processId)
        {
            if (!_processesByPid.TryGetValue(processId, out var process)
                || process.ParentProcessId <= 4
                || process.ParentProcessId == Environment.ProcessId
                || !_processesByPid.TryGetValue(process.ParentProcessId, out var parent))
            {
                return null;
            }

            return parent;
        }

        private static ServiceProcessInfo SelectPrimaryService(IEnumerable<ServiceProcessInfo> services) =>
            services
                .OrderByDescending(item => string.Equals(item.StartMode, "Auto", StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .First();

        private static Dictionary<int, List<ServiceProcessInfo>> ReadServicesByPid()
        {
            var servicesByPid = new Dictionary<int, List<ServiceProcessInfo>>();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, DisplayName, StartMode, State FROM Win32_Service WHERE ProcessId > 0");
                foreach (ManagementObject service in searcher.Get())
                {
                    var processId = Convert.ToInt32(service["ProcessId"] ?? 0);
                    if (processId <= 0)
                    {
                        continue;
                    }

                    var name = service["Name"]?.ToString() ?? "service";
                    var displayName = service["DisplayName"]?.ToString();
                    var startMode = service["StartMode"]?.ToString() ?? "Unknown";
                    var state = service["State"]?.ToString() ?? "Unknown";
                    if (!servicesByPid.TryGetValue(processId, out var services))
                    {
                        services = [];
                        servicesByPid.Add(processId, services);
                    }

                    services.Add(new ServiceProcessInfo(name, string.IsNullOrWhiteSpace(displayName) ? name : displayName, startMode, state));
                }
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
            {
                // Service restart hints are best effort.
            }

            return servicesByPid;
        }

        private static Dictionary<int, ProcessParentInfo> ReadProcessParents()
        {
            var processes = new Dictionary<int, ProcessParentInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name FROM Win32_Process");
                foreach (ManagementObject process in searcher.Get())
                {
                    var processId = Convert.ToInt32(process["ProcessId"] ?? 0);
                    if (processId <= 0)
                    {
                        continue;
                    }

                    var parentProcessId = Convert.ToInt32(process["ParentProcessId"] ?? 0);
                    var name = process["Name"]?.ToString() ?? $"PID {processId}";
                    processes[processId] = new ProcessParentInfo(processId, parentProcessId, name);
                }
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
            {
                // Parent process hints are best effort.
            }

            return processes;
        }
    }

    private sealed record ServiceProcessInfo(string Name, string DisplayName, string StartMode, string State);

    private sealed record ProcessParentInfo(int ProcessId, int ParentProcessId, string Name);
}
