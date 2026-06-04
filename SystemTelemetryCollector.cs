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
        var memory = ReadMemoryStatus();
        var gpuPercent = ReadGpuUtilization();
        var cpuPercent = ReadCpuPercent();
        var topProcesses = gpuSnapshot.Rows
            .OrderByDescending(row => row.LocalBytes)
            .ThenByDescending(row => row.DedicatedBytes)
            .Take(10)
            .Select(row => new GpuProcessInfo(
                row.ProcessId,
                row.ProcessName,
                row.LocalBytes,
                row.DedicatedBytes,
                row.SharedBytes,
                row.TotalCommittedBytes,
                row.WindowTitle,
                row.ExecutablePath,
                row.Notes,
                row.CanKill))
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
}
