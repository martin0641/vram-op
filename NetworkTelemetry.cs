using System.ComponentModel;
using System.Net.NetworkInformation;

namespace VramOp;

internal sealed class NetworkUsageReader : IDisposable
{
    private static readonly TimeSpan InterfaceRefreshInterval = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly Dictionary<string, InterfaceCounterSnapshot> _previous = new(StringComparer.OrdinalIgnoreCase);
    private NetworkInterface[] _cachedInterfaces = [];
    private DateTimeOffset _lastInterfaceRefresh = DateTimeOffset.MinValue;
    private NetworkSelectionMode _selectionMode = NetworkSelectionMode.Auto;
    private List<string> _selectedIds = [];
    private bool _disposed;

    public void ApplySettings(AppSettings settings)
    {
        lock (_gate)
        {
            _selectionMode = settings.NetworkSelectionMode;
            _selectedIds = settings.TrackedNetworkInterfaceIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
        }
    }

    public IReadOnlyList<NetworkInterfaceTelemetry> Read(NetworkSelectionOverride? selectionOverride = null)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return [];
            }

            var now = DateTimeOffset.UtcNow;
            var snapshots = ReadInterfaceSnapshots(now);
            var mode = selectionOverride?.Mode ?? _selectionMode;
            var selectedIds = selectionOverride?.InterfaceIds ?? _selectedIds;
            var selected = SelectInterfaces(snapshots, mode, selectedIds)
                .Take(4)
                .Select((snapshot, index) => snapshot.ToTelemetry($"NIC{index + 1}"))
                .ToArray();

            PrunePreviousSnapshots(snapshots);
            return selected;
        }
    }

    public static IReadOnlyList<NetworkInterfaceOption> GetAvailableInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsVisibleInterface)
            .OrderByDescending(item => item.OperationalStatus == OperationalStatus.Up)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new NetworkInterfaceOption(
                item.Id,
                item.Name,
                item.Description,
                item.OperationalStatus.ToString(),
                item.Speed))
            .ToArray();

    public void Dispose()
    {
        lock (_gate)
        {
            _previous.Clear();
            _cachedInterfaces = [];
            _disposed = true;
        }
    }

    private IReadOnlyList<InterfaceRateSnapshot> ReadInterfaceSnapshots(DateTimeOffset now)
    {
        var interfaces = GetVisibleInterfaces(now);
        var snapshots = new List<InterfaceRateSnapshot>(interfaces.Length);

        foreach (var networkInterface in interfaces)
        {
            try
            {
                var stats = networkInterface.GetIPv4Statistics();
                var current = new InterfaceCounterSnapshot(
                    now,
                    Math.Max(0, stats.BytesReceived),
                    Math.Max(0, stats.BytesSent));

                var receiveBytesPerSecond = 0D;
                var sendBytesPerSecond = 0D;
                if (_previous.TryGetValue(networkInterface.Id, out var previous))
                {
                    var elapsedSeconds = Math.Max(0.001, (now - previous.CapturedAt).TotalSeconds);
                    if (current.BytesReceived >= previous.BytesReceived)
                    {
                        receiveBytesPerSecond = (current.BytesReceived - previous.BytesReceived) / elapsedSeconds;
                    }

                    if (current.BytesSent >= previous.BytesSent)
                    {
                        sendBytesPerSecond = (current.BytesSent - previous.BytesSent) / elapsedSeconds;
                    }
                }

                _previous[networkInterface.Id] = current;
                snapshots.Add(new InterfaceRateSnapshot(
                    networkInterface.Id,
                    networkInterface.Name,
                    networkInterface.Description,
                    networkInterface.OperationalStatus,
                    Math.Max(0, networkInterface.Speed),
                    receiveBytesPerSecond,
                    sendBytesPerSecond));
            }
            catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or InvalidOperationException or Win32Exception)
            {
                // Interfaces can disappear between enumeration and statistics reads.
            }
        }

        return snapshots;
    }

    private NetworkInterface[] GetVisibleInterfaces(DateTimeOffset now)
    {
        if (_cachedInterfaces.Length > 0
            && now - _lastInterfaceRefresh < InterfaceRefreshInterval)
        {
            return _cachedInterfaces;
        }

        _cachedInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsVisibleInterface)
            .ToArray();
        _lastInterfaceRefresh = now;
        return _cachedInterfaces;
    }

    private IEnumerable<InterfaceRateSnapshot> SelectInterfaces(
        IReadOnlyList<InterfaceRateSnapshot> snapshots,
        NetworkSelectionMode selectionMode,
        IReadOnlyList<string> selectedIds)
    {
        if (selectionMode == NetworkSelectionMode.Manual)
        {
            foreach (var id in selectedIds)
            {
                var match = snapshots.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    yield return match;
                }
            }

            yield break;
        }

        foreach (var snapshot in snapshots
            .Where(item => item.OperationalStatus == OperationalStatus.Up)
            .OrderByDescending(item => item.ActivityBytesPerSecond)
            .ThenByDescending(item => item.LinkSpeedBitsPerSecond)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (snapshot.ActivityBytesPerSecond <= 0
                && snapshot.LinkSpeedBitsPerSecond <= 0)
            {
                continue;
            }

            yield return snapshot;
        }
    }

    private void PrunePreviousSnapshots(IReadOnlyList<InterfaceRateSnapshot> snapshots)
    {
        var liveIds = snapshots.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in _previous.Keys.Where(id => !liveIds.Contains(id)).ToArray())
        {
            _previous.Remove(staleId);
        }
    }

    private static bool IsVisibleInterface(NetworkInterface networkInterface) =>
        networkInterface.NetworkInterfaceType is not NetworkInterfaceType.Loopback
        && networkInterface.NetworkInterfaceType is not NetworkInterfaceType.Tunnel
        && !networkInterface.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
        && !networkInterface.Description.Contains("Loopback", StringComparison.OrdinalIgnoreCase);

    private readonly record struct InterfaceCounterSnapshot(
        DateTimeOffset CapturedAt,
        long BytesReceived,
        long BytesSent);

    private sealed record InterfaceRateSnapshot(
        string Id,
        string Name,
        string Description,
        OperationalStatus OperationalStatus,
        long LinkSpeedBitsPerSecond,
        double ReceiveBytesPerSecond,
        double SendBytesPerSecond)
    {
        public double ActivityBytesPerSecond => ReceiveBytesPerSecond + SendBytesPerSecond;

        public NetworkInterfaceTelemetry ToTelemetry(string label) =>
            new(Id, Name, Description, label, LinkSpeedBitsPerSecond, ReceiveBytesPerSecond, SendBytesPerSecond);
    }
}

internal sealed record NetworkInterfaceOption(
    string Id,
    string Name,
    string Description,
    string Status,
    long LinkSpeedBitsPerSecond)
{
    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Name) ? Description : Name;
            var speed = NetworkRateFormatter.FormatLinkSpeed(LinkSpeedBitsPerSecond);
            return string.IsNullOrWhiteSpace(speed)
                ? $"{name} ({Status})"
                : $"{name} ({Status}, {speed})";
        }
    }
}

internal sealed record NetworkSelectionOverride(
    NetworkSelectionMode Mode,
    IReadOnlyList<string> InterfaceIds);
