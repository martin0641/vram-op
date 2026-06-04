using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace VramOp;

internal sealed class GpuProcessMemoryReader
{
    private const string ProcessCategoryName = "GPU Process Memory";
    private const string AdapterCategoryName = "GPU Adapter Memory";
    private const string DedicatedCounter = "Dedicated Usage";
    private const string SharedCounter = "Shared Usage";
    private const string LocalCounter = "Local Usage";
    private const string NonLocalCounter = "Non Local Usage";
    private const string TotalCommittedCounter = "Total Committed";

    private static readonly Regex PidPattern = new(@"pid_(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss",
        "dwm",
        "fontdrvhost",
        "lsass",
        "registry",
        "services",
        "smss",
        "system",
        "wininit",
        "winlogon"
    };

    public GpuProcessMemorySnapshot Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return GpuProcessMemorySnapshot.Empty("GPU process memory counters are only available on Windows.");
        }

        try
        {
            if (!PerformanceCounterCategory.Exists(ProcessCategoryName))
            {
                return GpuProcessMemorySnapshot.Empty("Windows did not report the GPU Process Memory performance counter category.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return GpuProcessMemorySnapshot.Empty($"Unable to inspect GPU counters: {ex.Message}");
        }

        string[] instances;
        try
        {
            instances = new PerformanceCounterCategory(ProcessCategoryName).GetInstanceNames();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return GpuProcessMemorySnapshot.Empty($"Unable to enumerate GPU counter instances: {ex.Message}");
        }

        var adapterMemory = ReadAdapterMemory();
        var aggregates = new Dictionary<int, MutableGpuProcessMemory>();

        foreach (var instance in instances)
        {
            var match = PidPattern.Match(instance);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var pid))
            {
                continue;
            }

            if (!aggregates.TryGetValue(pid, out var aggregate))
            {
                aggregate = new MutableGpuProcessMemory(pid);
                aggregates.Add(pid, aggregate);
            }

            aggregate.InstanceCount++;
            aggregate.DedicatedBytes += ReadCounter(ProcessCategoryName, instance, DedicatedCounter);
            aggregate.SharedBytes += ReadCounter(ProcessCategoryName, instance, SharedCounter);
            aggregate.LocalBytes += ReadCounter(ProcessCategoryName, instance, LocalCounter);
            aggregate.NonLocalBytes += ReadCounter(ProcessCategoryName, instance, NonLocalCounter);
            aggregate.TotalCommittedBytes += ReadCounter(ProcessCategoryName, instance, TotalCommittedCounter);
        }

        var rows = aggregates.Values
            .Where(item => item.TotalObservedBytes > 0)
            .Select(ToRow)
            .Select(FlagCounterOutliers)
            .OrderByDescending(row => row.LocalBytes)
            .ThenByDescending(row => row.LocalBytes)
            .ThenBy(row => row.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new GpuProcessMemorySnapshot(rows, DateTimeOffset.Now, null, adapterMemory);
    }

    private static GpuAdapterMemorySnapshot ReadAdapterMemory()
    {
        try
        {
            if (!PerformanceCounterCategory.Exists(AdapterCategoryName))
            {
                return GpuAdapterMemorySnapshot.Empty;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return GpuAdapterMemorySnapshot.Empty;
        }

        string[] instances;
        try
        {
            instances = new PerformanceCounterCategory(AdapterCategoryName).GetInstanceNames();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return GpuAdapterMemorySnapshot.Empty;
        }

        long dedicatedBytes = 0;
        long sharedBytes = 0;
        long totalCommittedBytes = 0;

        foreach (var instance in instances)
        {
            dedicatedBytes += ReadCounter(AdapterCategoryName, instance, DedicatedCounter);
            sharedBytes += ReadCounter(AdapterCategoryName, instance, SharedCounter);
            totalCommittedBytes += ReadCounter(AdapterCategoryName, instance, TotalCommittedCounter);
        }

        return new GpuAdapterMemorySnapshot(dedicatedBytes, sharedBytes, totalCommittedBytes, instances.Length);
    }

    private static long ReadCounter(string categoryName, string instanceName, string counterName)
    {
        try
        {
            using var counter = new PerformanceCounter(categoryName, counterName, instanceName, readOnly: true);
            return Math.Max(0, counter.RawValue);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            return 0;
        }
    }

    private static GpuProcessMemoryRow ToRow(MutableGpuProcessMemory item)
    {
        var processName = $"PID {item.ProcessId}";
        var windowTitle = string.Empty;
        var executablePath = string.Empty;
        var notes = string.Empty;
        var canKill = item.ProcessId > 4 && item.ProcessId != Environment.ProcessId;
        var isProtected = false;

        try
        {
            using var process = Process.GetProcessById(item.ProcessId);
            processName = process.ProcessName;
            windowTitle = process.MainWindowTitle ?? string.Empty;

            if (ProtectedProcessNames.Contains(processName))
            {
                canKill = false;
                isProtected = true;
            }

            if (process.Id == Environment.ProcessId)
            {
                canKill = false;
                notes = "This app";
            }

            try
            {
                executablePath = process.MainModule?.FileName ?? string.Empty;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                notes = string.IsNullOrWhiteSpace(notes) ? "Path requires higher access" : notes;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            canKill = false;
            notes = "Process exited";
        }

        return new GpuProcessMemoryRow(
            item.ProcessId,
            processName,
            item.DedicatedBytes,
            item.SharedBytes,
            item.LocalBytes,
            item.NonLocalBytes,
            item.TotalCommittedBytes,
            item.InstanceCount,
            windowTitle,
            executablePath,
            notes,
            canKill,
            isProtected);
    }

    private static GpuProcessMemoryRow FlagCounterOutliers(GpuProcessMemoryRow row)
    {
        var likelyInflated = row.DedicatedBytes > 1024L * 1024 * 1024
            && row.DedicatedBytes > Math.Max(row.LocalBytes * 8, row.LocalBytes + 1024L * 1024 * 1024);

        if (!likelyInflated)
        {
            return row;
        }

        var notes = string.IsNullOrWhiteSpace(row.Notes)
            ? "Dedicated counter likely inflated"
            : $"{row.Notes}; dedicated counter likely inflated";

        return row with { Notes = notes };
    }

    private sealed class MutableGpuProcessMemory(int processId)
    {
        public int ProcessId { get; } = processId;
        public long DedicatedBytes { get; set; }
        public long SharedBytes { get; set; }
        public long LocalBytes { get; set; }
        public long NonLocalBytes { get; set; }
        public long TotalCommittedBytes { get; set; }
        public int InstanceCount { get; set; }

        public long TotalObservedBytes =>
            DedicatedBytes + SharedBytes + LocalBytes + NonLocalBytes + TotalCommittedBytes;
    }
}

internal sealed record GpuProcessMemorySnapshot(
    IReadOnlyList<GpuProcessMemoryRow> Rows,
    DateTimeOffset CapturedAt,
    string? ErrorMessage,
    GpuAdapterMemorySnapshot AdapterMemory)
{
    public static GpuProcessMemorySnapshot Empty(string? errorMessage = null) =>
        new(Array.Empty<GpuProcessMemoryRow>(), DateTimeOffset.Now, errorMessage, GpuAdapterMemorySnapshot.Empty);
}

internal sealed record GpuAdapterMemorySnapshot(
    long DedicatedBytes,
    long SharedBytes,
    long TotalCommittedBytes,
    int AdapterCount)
{
    public static GpuAdapterMemorySnapshot Empty { get; } = new(0, 0, 0, 0);
}

internal sealed record GpuProcessMemoryRow(
    int ProcessId,
    string ProcessName,
    long DedicatedBytes,
    long SharedBytes,
    long LocalBytes,
    long NonLocalBytes,
    long TotalCommittedBytes,
    int InstanceCount,
    string WindowTitle,
    string ExecutablePath,
    string Notes,
    bool CanKill,
    bool IsProtected);
