using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace VramOp;

internal sealed class SystemTelemetryCollector : IDisposable
{
    private readonly GpuProcessMemoryReader _gpuMemoryReader = new();
    private readonly GpuUtilizationReader _gpuUtilizationReader = new();
    private readonly NetworkUsageReader _networkUsageReader = new();
    private readonly CpuUsageReader _cpuUsageReader = new();
    private readonly UsageSampler _usageSampler;
    private readonly List<GpuAdapterInfo> _adapters;
    private readonly string _hostId;
    private bool _disposed;

    public SystemTelemetryCollector()
    {
        _hostId = $"{Environment.MachineName}-{Environment.UserName}";
        _adapters = ReadGpuAdapters();

        _usageSampler = new UsageSampler(ReadCpuPercent, _gpuUtilizationReader.ReadPercent);
    }

    public HostTelemetry Read(NetworkSelectionOverride? networkSelectionOverride = null)
    {
        var averagedUsage = _usageSampler.ReadAverage();
        var gpuSnapshot = _gpuMemoryReader.Read();
        var restartIndex = ProcessRestartIndex.Read();
        var memory = ReadMemoryStatus();
        var networkInterfaces = _networkUsageReader.Read(networkSelectionOverride);
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
            averagedUsage.CpuPercent,
            averagedUsage.GpuPercent,
            memory.UsedBytes,
            memory.TotalBytes,
            gpuSnapshot.AdapterMemory.DedicatedBytes,
            _adapters.Sum(adapter => adapter.VramTotalBytes),
            gpuSnapshot.AdapterMemory.SharedBytes,
            _adapters,
            networkInterfaces,
            topProcesses,
            gpuSnapshot.ErrorMessage);
    }

    public void ApplySettings(AppSettings settings)
    {
        _networkUsageReader.ApplySettings(settings);
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

        _usageSampler.Dispose();
        _networkUsageReader.Dispose();
        _gpuUtilizationReader.Dispose();
        _cpuUsageReader.Dispose();
        _disposed = true;
    }

    private double ReadCpuPercent() => _cpuUsageReader.ReadPercent();

    private static List<GpuAdapterInfo> ReadGpuAdapters()
    {
        var dxgiAdapters = ReadDxgiGpuAdapters();
        if (dxgiAdapters.Count > 0)
        {
            return dxgiAdapters;
        }

        return ReadWmiGpuAdapters();
    }

    private static List<GpuAdapterInfo> ReadWmiGpuAdapters()
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

    private static List<GpuAdapterInfo> ReadDxgiGpuAdapters()
    {
        var adapters = new List<GpuAdapterInfo>();
        var wmiAdapters = ReadWmiGpuAdapters();
        IDXGIFactory1? factory = null;

        try
        {
            var seenAdapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var factoryId = typeof(IDXGIFactory1).GUID;
            var hr = CreateDXGIFactory1(ref factoryId, out factory);
            if (hr < 0 || factory is null)
            {
                return adapters;
            }

            for (uint index = 0; ; index++)
            {
                IDXGIAdapter1? adapter = null;
                try
                {
                    hr = factory.EnumAdapters1(index, out adapter);
                    if (hr == DXGI_ERROR_NOT_FOUND)
                    {
                        break;
                    }

                    if (hr < 0 || adapter is null)
                    {
                        continue;
                    }

                    hr = adapter.GetDesc1(out var description);
                    if (hr < 0 || (description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
                    {
                        continue;
                    }

                    var dedicatedBytes = UIntPtrToInt64(description.DedicatedVideoMemory);
                    if (dedicatedBytes <= 0)
                    {
                        continue;
                    }

                    var adapterKey = $"{description.AdapterLuid.HighPart:X8}:{description.AdapterLuid.LowPart:X8}";
                    if (!seenAdapters.Add(adapterKey))
                    {
                        continue;
                    }

                    var name = description.Description.Trim();
                    var driverVersion = MatchDriverVersion(name, wmiAdapters);
                    adapters.Add(new GpuAdapterInfo(name, dedicatedBytes, driverVersion));
                }
                finally
                {
                    if (adapter is not null)
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException or UnauthorizedAccessException)
        {
            // DXGI is the reliable source for large VRAM totals, but older or restricted systems can block it.
        }
        finally
        {
            if (factory is not null)
            {
                Marshal.ReleaseComObject(factory);
            }
        }

        return DeduplicateDxgiAdapters(adapters, wmiAdapters);
    }

    private static string MatchDriverVersion(string adapterName, IReadOnlyList<GpuAdapterInfo> wmiAdapters)
    {
        var match = wmiAdapters.FirstOrDefault(adapter =>
            adapterName.Contains(adapter.Name, StringComparison.OrdinalIgnoreCase)
            || adapter.Name.Contains(adapterName, StringComparison.OrdinalIgnoreCase));

        return match?.DriverVersion ?? string.Empty;
    }

    private static List<GpuAdapterInfo> DeduplicateDxgiAdapters(IReadOnlyList<GpuAdapterInfo> adapters, IReadOnlyList<GpuAdapterInfo> wmiAdapters)
    {
        var result = new List<GpuAdapterInfo>();

        foreach (var group in adapters.GroupBy(adapter => $"{NormalizeAdapterName(adapter.Name)}|{adapter.VramTotalBytes}"))
        {
            var first = group.First();
            var expectedCount = wmiAdapters.Count(adapter => AdapterNamesMatch(first.Name, adapter.Name));
            var takeCount = expectedCount > 0 ? expectedCount : 1;
            result.AddRange(group.Take(takeCount));
        }

        return result;
    }

    private static bool AdapterNamesMatch(string left, string right) =>
        left.Contains(right, StringComparison.OrdinalIgnoreCase)
        || right.Contains(left, StringComparison.OrdinalIgnoreCase)
        || string.Equals(NormalizeAdapterName(left), NormalizeAdapterName(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAdapterName(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static long UIntPtrToInt64(UIntPtr value)
    {
        var bytes = value.ToUInt64();
        return bytes > long.MaxValue ? long.MaxValue : (long)bytes;
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
    private static extern bool GetSystemTimes(
        out FileTime lpIdleTime,
        out FileTime lpKernelTime,
        out FileTime lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1? factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

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

    private sealed class UsageSampler : IDisposable
    {
        private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

        private readonly Func<double> _readCpuPercent;
        private readonly Func<double> _readGpuPercent;
        private readonly Queue<UsageSample> _samples = [];
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _sampleTask;
        private UsageSample? _lastSample;
        private bool _disposed;

        public UsageSampler(Func<double> readCpuPercent, Func<double> readGpuPercent)
        {
            _readCpuPercent = readCpuPercent;
            _readGpuPercent = readGpuPercent;
            SampleNow();
            _sampleTask = Task.Run(RunAsync);
        }

        public UsageSample ReadAverage()
        {
            var now = DateTimeOffset.Now;

            lock (_gate)
            {
                Prune(now);

                return _samples.Count == 0
                    ? _lastSample ?? new UsageSample(now, 0, 0)
                    : new UsageSample(
                        now,
                        _samples.Average(sample => sample.CpuPercent),
                        _samples.Average(sample => sample.GpuPercent));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _cts.Cancel();
            try
            {
                _sampleTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
                // Shutdown is already in progress; the sampler will be torn down with the process.
            }

            _cts.Dispose();
            _disposed = true;
        }

        private async Task RunAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(SampleInterval);
                while (await timer.WaitForNextTickAsync(_cts.Token))
                {
                    SampleNow();
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        private void SampleNow()
        {
            var now = DateTimeOffset.Now;
            var sample = new UsageSample(
                now,
                ClampPercent(ReadUsage(_readCpuPercent)),
                ClampPercent(ReadUsage(_readGpuPercent)));

            lock (_gate)
            {
                _lastSample = sample;
                _samples.Enqueue(sample);
                Prune(now);
            }
        }

        private void Prune(DateTimeOffset now)
        {
            var cutoff = now - Window;
            while (_samples.Count > 0 && _samples.Peek().CapturedAt < cutoff)
            {
                _samples.Dequeue();
            }
        }

        private static double ReadUsage(Func<double> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
            {
                return 0;
            }
        }
    }

    private readonly record struct UsageSample(
        DateTimeOffset CapturedAt,
        double CpuPercent,
        double GpuPercent);

    private sealed class CpuUsageReader : IDisposable
    {
        private readonly object _gate = new();
        private readonly PerformanceCounter? _fallbackCounter;
        private CpuTimes? _previous;
        private bool _disposed;

        public CpuUsageReader()
        {
            _previous = TryReadCpuTimes();

            try
            {
                _fallbackCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
                _fallbackCounter.NextValue();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
            {
                _fallbackCounter = null;
            }
        }

        public double ReadPercent()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return 0;
                }

                var current = TryReadCpuTimes();
                if (current is not null)
                {
                    if (_previous is { } previous)
                    {
                        _previous = current;
                        if (current.Value.Idle >= previous.Idle
                            && current.Value.Kernel >= previous.Kernel
                            && current.Value.User >= previous.User)
                        {
                            var idleDelta = current.Value.Idle - previous.Idle;
                            var kernelDelta = current.Value.Kernel - previous.Kernel;
                            var userDelta = current.Value.User - previous.User;
                            var totalDelta = kernelDelta + userDelta;

                            if (totalDelta > 0 && idleDelta <= totalDelta)
                            {
                                return ClampPercent((totalDelta - idleDelta) * 100D / totalDelta);
                            }
                        }
                    }
                    else
                    {
                        _previous = current;
                    }
                }

                return ReadFallbackCounter();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _fallbackCounter?.Dispose();
                _disposed = true;
            }
        }

        private double ReadFallbackCounter()
        {
            try
            {
                return ClampPercent(_fallbackCounter?.NextValue() ?? 0);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
            {
                return 0;
            }
        }

        private static CpuTimes? TryReadCpuTimes()
        {
            return GetSystemTimes(out var idle, out var kernel, out var user)
                ? new CpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user))
                : null;
        }

        private static ulong ToUInt64(FileTime value) =>
            ((ulong)value.HighDateTime << 32) | value.LowDateTime;
    }

    private readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

    private sealed class GpuUtilizationReader : IDisposable
    {
        private const string CategoryName = "GPU Engine";
        private const string CounterName = "Utilization Percentage";
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

        private readonly Dictionary<string, PerformanceCounter> _counters = [];
        private readonly object _gate = new();
        private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
        private bool _disposed;

        public double ReadPercent()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return 0;
                }

                try
                {
                    if (DateTimeOffset.Now - _lastRefresh >= RefreshInterval)
                    {
                        RefreshCounters();
                    }

                    double total = 0;
                    List<string>? staleCounters = null;

                    foreach (var (instance, counter) in _counters)
                    {
                        try
                        {
                            total += counter.NextValue();
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
                        {
                            staleCounters ??= [];
                            staleCounters.Add(instance);
                        }
                    }

                    if (staleCounters is not null)
                    {
                        foreach (var instance in staleCounters)
                        {
                            RemoveCounter(instance);
                        }
                    }

                    return ClampPercent(total);
                }
                catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
                {
                    return 0;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                foreach (var counter in _counters.Values)
                {
                    counter.Dispose();
                }

                _counters.Clear();
                _disposed = true;
            }
        }

        private void RefreshCounters()
        {
            _lastRefresh = DateTimeOffset.Now;

            if (!PerformanceCounterCategory.Exists(CategoryName))
            {
                ClearCounters();
                return;
            }

            var category = new PerformanceCounterCategory(CategoryName);
            var liveInstances = category.GetInstanceNames()
                .Where(instance => instance.Contains("engtype_", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var instance in _counters.Keys.Where(instance => !liveInstances.Contains(instance)).ToArray())
            {
                RemoveCounter(instance);
            }

            foreach (var instance in liveInstances)
            {
                if (_counters.ContainsKey(instance))
                {
                    continue;
                }

                try
                {
                    var counter = new PerformanceCounter(CategoryName, CounterName, instance, readOnly: true);
                    counter.NextValue();
                    _counters.Add(instance, counter);
                }
                catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
                {
                    // GPU engine instances are short lived; a missed counter will be retried on the next refresh.
                }
            }
        }

        private void ClearCounters()
        {
            foreach (var counter in _counters.Values)
            {
                counter.Dispose();
            }

            _counters.Clear();
        }

        private void RemoveCounter(string instance)
        {
            if (!_counters.Remove(instance, out var counter))
            {
                return;
            }

            counter.Dispose();
        }
    }

    [ComImport]
    [Guid("29038F61-3839-4626-91FD-086879011A05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        [PreserveSig]
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);

        [PreserveSig]
        int SetPrivateDataInterface(ref Guid name, IntPtr unknown);

        [PreserveSig]
        int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);

        [PreserveSig]
        int GetParent(ref Guid riid, out IntPtr parent);

        [PreserveSig]
        int EnumOutputs(uint output, out IntPtr outputPointer);

        [PreserveSig]
        int GetDesc(out DxgiAdapterDescription description);

        [PreserveSig]
        int CheckInterfaceSupport(ref Guid interfaceName, out long umdVersion);

        [PreserveSig]
        int GetDesc1(out DxgiAdapterDescription1 description);
    }

    [ComImport]
    [Guid("770AAE78-F26F-4DBA-A829-253C83D1B387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        [PreserveSig]
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);

        [PreserveSig]
        int SetPrivateDataInterface(ref Guid name, IntPtr unknown);

        [PreserveSig]
        int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);

        [PreserveSig]
        int GetParent(ref Guid riid, out IntPtr parent);

        [PreserveSig]
        int EnumAdapters(uint adapter, out IntPtr adapterPointer);

        [PreserveSig]
        int MakeWindowAssociation(IntPtr windowHandle, uint flags);

        [PreserveSig]
        int GetWindowAssociation(out IntPtr windowHandle);

        [PreserveSig]
        int CreateSwapChain(IntPtr device, IntPtr description, out IntPtr swapChain);

        [PreserveSig]
        int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);

        [PreserveSig]
        int EnumAdapters1(uint adapter, out IDXGIAdapter1? adapterPointer);

        [PreserveSig]
        int IsCurrent();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public Luid AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDescription1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

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
