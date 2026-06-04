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

    public static string Percent(double value) => $"{Math.Round(value):N0}%";

    public static double Ratio(long used, long total) =>
        total <= 0 ? 0 : Math.Min(1, Math.Max(0, used / (double)total));
}
