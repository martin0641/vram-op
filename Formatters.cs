namespace VramOp;

internal static class Formatters
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes:N0} {units[unitIndex]}"
            : value >= 10 ? $"{value:N0} {units[unitIndex]}" : $"{value:N1} {units[unitIndex]}";
    }

    public static string BytesPrecise(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{bytes:N0} {units[unitIndex]}";
        }

        return unitIndex >= 3 || value < 10
            ? $"{value:N1} {units[unitIndex]}"
            : $"{value:N0} {units[unitIndex]}";
    }

    public static string Percent(double value)
    {
        value = Math.Clamp(value, 0, 100);
        if (value < 0.05)
        {
            return "0%";
        }

        return value < 10
            ? $"{value:N1}%"
            : $"{value:N0}%";
    }

    public static double Ratio(long used, long total) =>
        total <= 0 ? 0 : Math.Min(1, Math.Max(0, used / (double)total));
}

internal static class NetworkRateFormatter
{
    public static string FormatPair(double receiveBytesPerSecond, double sendBytesPerSecond, NetworkRateUnit unit) =>
        $"R {Format(receiveBytesPerSecond, unit)}/S {Format(sendBytesPerSecond, unit)}";

    public static string FormatWidgetPair(double receiveBytesPerSecond, double sendBytesPerSecond, NetworkRateUnit unit)
    {
        var (receive, suffix) = Convert(receiveBytesPerSecond, unit);
        var (send, _) = Convert(sendBytesPerSecond, unit);
        return $"R {receive:N2}/{send:N2} S {suffix}";
    }

    public static string Format(double bytesPerSecond, NetworkRateUnit unit)
    {
        var (value, suffix) = Convert(bytesPerSecond, unit);
        var format = value switch
        {
            >= 100 => "N0",
            >= 10 => "N1",
            _ => "N2"
        };
        return $"{value.ToString(format)} {suffix}";
    }

    public static string FormatLinkSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return string.Empty;
        }

        if (bitsPerSecond >= 1_000_000_000)
        {
            return $"{bitsPerSecond / 1_000_000_000D:N1} Gbps";
        }

        if (bitsPerSecond >= 1_000_000)
        {
            return $"{bitsPerSecond / 1_000_000D:N0} Mbps";
        }

        return $"{bitsPerSecond / 1_000D:N0} Kbps";
    }

    public static double RatioToLink(double bytesPerSecond, long linkSpeedBitsPerSecond)
    {
        if (linkSpeedBitsPerSecond <= 0)
        {
            return 0;
        }

        return Math.Min(1, Math.Max(0, bytesPerSecond * 8D / linkSpeedBitsPerSecond));
    }

    public static string DisplayName(NetworkRateUnit unit) =>
        unit switch
        {
            NetworkRateUnit.Mbps => "Mbps",
            NetworkRateUnit.MBps => "MBps",
            NetworkRateUnit.Gbps => "Gbps",
            NetworkRateUnit.GBps => "GBps",
            NetworkRateUnit.Mibps => "Mibps",
            NetworkRateUnit.MiBps => "MiBps",
            NetworkRateUnit.Gibps => "Gibps",
            NetworkRateUnit.GiBps => "GiBps",
            _ => unit.ToString()
        };

    private static (double Value, string Suffix) Convert(double bytesPerSecond, NetworkRateUnit unit) =>
        unit switch
        {
            NetworkRateUnit.Mbps => (bytesPerSecond * 8D / 1_000_000D, "Mbps"),
            NetworkRateUnit.MBps => (bytesPerSecond / 1_000_000D, "MBps"),
            NetworkRateUnit.Gbps => (bytesPerSecond * 8D / 1_000_000_000D, "Gbps"),
            NetworkRateUnit.GBps => (bytesPerSecond / 1_000_000_000D, "GBps"),
            NetworkRateUnit.Mibps => (bytesPerSecond * 8D / 1_048_576D, "Mibps"),
            NetworkRateUnit.MiBps => (bytesPerSecond / 1_048_576D, "MiBps"),
            NetworkRateUnit.Gibps => (bytesPerSecond * 8D / 1_073_741_824D, "Gibps"),
            NetworkRateUnit.GiBps => (bytesPerSecond / 1_073_741_824D, "GiBps"),
            _ => (bytesPerSecond * 8D / 1_000_000D, "Mbps")
        };
}
